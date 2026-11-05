namespace CentralGit.Application.Git;

/// <summary>The outcome of one git invocation, kept verbatim so the UI can show exact diagnostics.</summary>
public sealed record GitResult(
    IReadOnlyList<string> Arguments,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration
)
{
    public bool Succeeded => ExitCode == 0;

    /// <summary>The command line as a user would type it, for the Operations Center.</summary>
    public string CommandLine => CommandLineOf(Arguments);

    public static string CommandLineOf(IEnumerable<string> arguments) =>
        "git " + string.Join(' ', arguments.Select(Quote));

    private static string Quote(string arg) => arg.Length == 0 || arg.Any(char.IsWhiteSpace) ? $"\"{arg}\"" : arg;
}

/// <summary>A git command a read depended on failed. Carries the full result for the error message.</summary>
public sealed class GitException(GitResult result)
    : Exception($"{result.CommandLine} failed with exit code {result.ExitCode}: {result.StandardError.Trim()}")
{
    public GitResult Result { get; } = result;
}
