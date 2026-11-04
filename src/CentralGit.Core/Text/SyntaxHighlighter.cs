namespace CentralGit.Core.Text;

public enum TokenKind { Plain, Keyword, Type, String, Comment, Number, Function, Punctuation, Tag, Attribute }

public readonly record struct SyntaxToken(int Start, int Length, TokenKind Kind);

public enum SyntaxLanguage { Plain, CLike, Xml, Json, Python, Sql, Shell }

/// <summary>
/// Small line-based highlighter for the diff view. Deliberately simple: multi-line constructs
/// (block comments, verbatim strings) are not tracked across lines. A TextMate-grammar based
/// highlighter can replace it behind the same API.
/// </summary>
public static class SyntaxHighlighter
{
    public static SyntaxLanguage FromPath(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".cs" or ".csx" or ".java" or ".kt" or ".kts" or ".js" or ".jsx" or ".mjs" or ".ts" or ".tsx" or ".go" or ".rs"
            or ".c" or ".h" or ".cpp" or ".hpp" or ".cc" or ".swift" or ".dart" or ".php" or ".scala" or ".groovy" or ".gradle" => SyntaxLanguage.CLike,
        ".xml" or ".xaml" or ".csproj" or ".props" or ".targets" or ".html" or ".htm" or ".svg" or ".resx" or ".config" or ".slnx" or ".vue" => SyntaxLanguage.Xml,
        ".json" or ".jsonc" => SyntaxLanguage.Json,
        ".py" => SyntaxLanguage.Python,
        ".sql" => SyntaxLanguage.Sql,
        ".sh" or ".bash" or ".ps1" or ".psm1" or ".yml" or ".yaml" or ".toml" or ".ini" or ".dockerfile" => SyntaxLanguage.Shell,
        _ => Path.GetFileName(path).Equals("Dockerfile", StringComparison.OrdinalIgnoreCase) ? SyntaxLanguage.Shell : SyntaxLanguage.Plain,
    };

    private static readonly HashSet<string> CLikeKeywords =
    [
        "abstract", "as", "async", "await", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
        "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false",
        "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is",
        "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected",
        "public", "readonly", "record", "ref", "return", "sbyte", "sealed", "short", "sizeof", "static", "string", "struct",
        "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "var",
        "virtual", "void", "volatile", "while", "yield", "get", "set", "init", "required", "when", "with", "and", "or", "not",
        "let", "function", "export", "import", "from", "extends", "implements", "package", "func", "fn", "pub", "mut", "impl",
        "match", "type", "val", "def", "undefined", "nil", "self", "super", "final", "throws", "go", "defer", "chan", "map",
        "range", "struct", "trait", "where", "loop", "elif", "constructor", "declare", "keyof", "readonly", "satisfies",
    ];

    private static readonly HashSet<string> PythonKeywords =
    [
        "and", "as", "assert", "async", "await", "break", "class", "continue", "def", "del", "elif", "else", "except", "False",
        "finally", "for", "from", "global", "if", "import", "in", "is", "lambda", "None", "nonlocal", "not", "or", "pass",
        "raise", "return", "True", "try", "while", "with", "yield", "self",
    ];

    private static readonly HashSet<string> SqlKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "select", "from", "where", "and", "or", "not", "insert", "into", "values", "update", "set", "delete", "create", "table",
        "alter", "drop", "index", "join", "left", "right", "inner", "outer", "on", "group", "by", "order", "having", "as",
        "distinct", "null", "is", "in", "exists", "primary", "key", "foreign", "references", "default", "union", "all", "limit",
        "case", "when", "then", "else", "end", "begin", "commit", "rollback", "varchar", "int", "bigint", "text", "boolean", "jsonb",
    };

    public static IReadOnlyList<SyntaxToken> Highlight(string line, SyntaxLanguage language) => language switch
    {
        SyntaxLanguage.Plain => [new SyntaxToken(0, line.Length, TokenKind.Plain)],
        SyntaxLanguage.Xml => HighlightXml(line),
        _ => HighlightCode(line, language),
    };

    private static List<SyntaxToken> HighlightCode(string s, SyntaxLanguage lang)
    {
        var tokens = new List<SyntaxToken>();
        var lineComment = lang switch
        {
            SyntaxLanguage.Python or SyntaxLanguage.Shell => "#",
            SyntaxLanguage.Sql => "--",
            SyntaxLanguage.Json => null,
            _ => "//",
        };
        var keywords = lang switch
        {
            SyntaxLanguage.Python => PythonKeywords,
            SyntaxLanguage.Sql => SqlKeywords,
            SyntaxLanguage.Json or SyntaxLanguage.Shell => null,
            _ => CLikeKeywords,
        };

        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            var start = i;

            if (lineComment is not null && string.CompareOrdinal(s, i, lineComment, 0, lineComment.Length) == 0)
            {
                tokens.Add(new SyntaxToken(i, s.Length - i, TokenKind.Comment));
                break;
            }
            if (lang == SyntaxLanguage.CLike && c == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                var close = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = close < 0 ? s.Length : close + 2;
                tokens.Add(new SyntaxToken(start, i - start, TokenKind.Comment));
                continue;
            }
            if (c is '"' or '\'' or '`' || (c is '$' or '@' && i + 1 < s.Length && s[i + 1] == '"'))
            {
                if (c is '$' or '@') i++;
                var quote = s[i++];
                while (i < s.Length && s[i] != quote)
                    i += s[i] == '\\' && i + 1 < s.Length ? 2 : 1;
                i = Math.Min(i + 1, s.Length);
                // JSON keys read as attributes: "key":
                var kind = lang == SyntaxLanguage.Json && NextNonSpace(s, i) == ':' ? TokenKind.Attribute : TokenKind.String;
                tokens.Add(new SyntaxToken(start, i - start, kind));
                continue;
            }
            if (char.IsDigit(c))
            {
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] is '.' or '_')) i++;
                tokens.Add(new SyntaxToken(start, i - start, TokenKind.Number));
                continue;
            }
            if (char.IsLetter(c) || c is '_')
            {
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] is '_')) i++;
                var word = s[start..i];
                var kind =
                    keywords?.Contains(word) == true || (lang == SyntaxLanguage.Json && word is "true" or "false" or "null") ? TokenKind.Keyword
                    : NextNonSpace(s, i) == '(' ? TokenKind.Function
                    : char.IsUpper(word[0]) && lang != SyntaxLanguage.Sql ? TokenKind.Type
                    : TokenKind.Plain;
                tokens.Add(new SyntaxToken(start, i - start, kind));
                continue;
            }
            if (char.IsWhiteSpace(c))
            {
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
                tokens.Add(new SyntaxToken(start, i - start, TokenKind.Plain));
                continue;
            }

            i++;
            tokens.Add(new SyntaxToken(start, 1, TokenKind.Punctuation));
        }
        return tokens;
    }

    private static List<SyntaxToken> HighlightXml(string s)
    {
        var tokens = new List<SyntaxToken>();
        var i = 0;
        var inTag = false;
        while (i < s.Length)
        {
            var start = i;
            if (!inTag)
            {
                if (string.CompareOrdinal(s, i, "<!--", 0, 4) == 0)
                {
                    var close = s.IndexOf("-->", i, StringComparison.Ordinal);
                    i = close < 0 ? s.Length : close + 3;
                    tokens.Add(new SyntaxToken(start, i - start, TokenKind.Comment));
                }
                else if (s[i] == '<')
                {
                    i++;
                    while (i < s.Length && (s[i] is '/' or '?' or '!')) i++;
                    while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] is not ('>' or '/')) i++;
                    tokens.Add(new SyntaxToken(start, i - start, TokenKind.Tag));
                    inTag = true;
                }
                else
                {
                    while (i < s.Length && s[i] != '<') i++;
                    tokens.Add(new SyntaxToken(start, i - start, TokenKind.Plain));
                }
                continue;
            }

            if (s[i] is '>' || (s[i] is '/' or '?' && i + 1 < s.Length && s[i + 1] == '>'))
            {
                i += s[i] == '>' ? 1 : 2;
                tokens.Add(new SyntaxToken(start, i - start, TokenKind.Tag));
                inTag = false;
            }
            else if (s[i] is '"' or '\'')
            {
                var q = s[i++];
                while (i < s.Length && s[i] != q) i++;
                i = Math.Min(i + 1, s.Length);
                tokens.Add(new SyntaxToken(start, i - start, TokenKind.String));
            }
            else if (char.IsLetter(s[i]) || s[i] is '_' or ':')
            {
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] is '_' or ':' or '-' or '.')) i++;
                tokens.Add(new SyntaxToken(start, i - start, TokenKind.Attribute));
            }
            else
            {
                i++;
                tokens.Add(new SyntaxToken(start, 1, TokenKind.Punctuation));
            }
        }
        return tokens;
    }

    private static char NextNonSpace(string s, int i)
    {
        while (i < s.Length && s[i] == ' ') i++;
        return i < s.Length ? s[i] : '\0';
    }
}
