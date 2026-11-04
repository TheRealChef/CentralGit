using System.Globalization;

namespace CentralGit.Core.Git;

public enum SignatureState { Unsigned, Good, GoodUnknownValidity, Bad, ExpiredSignature, ExpiredKey, RevokedKey, CannotCheck }

public sealed record ChangedFile(string Path, string? OldPath, ChangeKind Kind, int Added, int Deleted, bool IsBinary)
{
    public string FileName => System.IO.Path.GetFileName(Path);
    public string Directory => Path.Contains('/') ? Path[..Path.LastIndexOf('/')] : "";
}

public sealed record CommitDetails(
    string Hash,
    IReadOnlyList<string> Parents,
    Signature Author,
    Signature Committer,
    string Subject,
    string Body,
    SignatureState Signature,
    string? Signer,
    IReadOnlyList<ChangedFile> Files)
{
    /// <summary>The well-known hash of the empty tree, used as the "parent" of root commits.</summary>
    public const string EmptyTree = "4b825dc642cb6eb9a060e54bf8d69288fbee4904";

    /// <summary>What the diff is taken against: first parent, or the empty tree for a root commit.</summary>
    public string DiffBase => Parents.Count > 0 ? Parents[0] : EmptyTree;

    public int TotalAdded => Files.Sum(f => f.Added);
    public int TotalDeleted => Files.Sum(f => f.Deleted);

    private const string Format = "%H%x1f%P%x1f%an%x1f%ae%x1f%at%x1f%cn%x1f%ce%x1f%ct%x1f%G?%x1f%GS%x1f%B";

    public static async Task<CommitDetails> LoadAsync(GitRunner git, string repo, string hash, CancellationToken ct = default)
    {
        var header = await git.RunCheckedAsync(repo, ["show", "-s", $"--format={Format}", hash], ct).ConfigureAwait(false);
        var f = header.Split('\u001f', 11);
        var parents = f[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var (subject, body) = SplitMessage(f[10]);

        // Merges are shown against their first parent, like most clients (and `git show --first-parent`).
        var range = parents.Length > 0 ? new[] { parents[0], hash } : [EmptyTree, hash];
        var numstat = git.RunCheckedAsync(repo, ["diff-tree", "-r", "-M", "-z", "--numstat", .. range], ct);
        var nameStatus = git.RunCheckedAsync(repo, ["diff-tree", "-r", "-M", "-z", "--name-status", .. range], ct);

        var files = MergeFileLists(ParseNameStatus(await nameStatus.ConfigureAwait(false)), ParseNumstat(await numstat.ConfigureAwait(false)));

        return new CommitDetails(
            f[0], parents,
            new Signature(f[2], f[3], Unix(f[4])),
            new Signature(f[5], f[6], Unix(f[7])),
            subject, body,
            ParseSignature(f[8]), f[9].Length == 0 ? null : f[9],
            files);
    }

    internal static (string Subject, string Body) SplitMessage(string message)
    {
        message = message.Replace("\r\n", "\n").TrimEnd();
        var split = message.IndexOf("\n\n", StringComparison.Ordinal);
        return split < 0
            ? (message.Replace('\n', ' '), "")
            : (message[..split].Replace('\n', ' '), message[(split + 2)..].Trim('\n'));
    }

    internal static SignatureState ParseSignature(string code) => code switch
    {
        "G" => SignatureState.Good,
        "U" => SignatureState.GoodUnknownValidity,
        "B" => SignatureState.Bad,
        "X" => SignatureState.ExpiredSignature,
        "Y" => SignatureState.ExpiredKey,
        "R" => SignatureState.RevokedKey,
        "E" => SignatureState.CannotCheck,
        _ => SignatureState.Unsigned,
    };

    /// <summary><c>-z --name-status</c>: "M\0path\0" or "R100\0old\0new\0".</summary>
    internal static IReadOnlyList<(ChangeKind Kind, string Path, string? OldPath)> ParseNameStatus(string output)
    {
        var parts = output.Split('\0');
        var result = new List<(ChangeKind, string, string?)>();
        for (var i = 0; i < parts.Length; i++)
        {
            var status = parts[i];
            if (status.Length == 0)
                continue;

            var kind = status[0] switch
            {
                'A' => ChangeKind.Added,
                'D' => ChangeKind.Deleted,
                'R' => ChangeKind.Renamed,
                'C' => ChangeKind.Copied,
                'T' => ChangeKind.TypeChanged,
                'U' => ChangeKind.Unmerged,
                _ => ChangeKind.Modified,
            };

            if (kind is ChangeKind.Renamed or ChangeKind.Copied)
            {
                var old = parts[++i];
                result.Add((kind, parts[++i], old));
            }
            else
            {
                result.Add((kind, parts[++i], null));
            }
        }
        return result;
    }

    /// <summary><c>-z --numstat</c>: "a\td\tpath\0", renames "a\td\t\0old\0new\0", binary "-\t-\t…".</summary>
    internal static IReadOnlyDictionary<string, (int Added, int Deleted, bool Binary)> ParseNumstat(string output)
    {
        var parts = output.Split('\0');
        var result = new Dictionary<string, (int, int, bool)>(StringComparer.Ordinal);
        for (var i = 0; i < parts.Length; i++)
        {
            var line = parts[i];
            if (line.Length == 0)
                continue;

            var cols = line.Split('\t', 3);
            if (cols.Length < 3)
                continue;

            var path = cols[2];
            if (path.Length == 0 && i + 2 < parts.Length) // rename: old and new follow
            {
                i++;
                path = parts[++i];
            }

            var binary = cols[0] == "-";
            result[path] = binary ? (0, 0, true) : (Int(cols[0]), Int(cols[1]), false);
        }
        return result;
    }

    private static IReadOnlyList<ChangedFile> MergeFileLists(
        IReadOnlyList<(ChangeKind Kind, string Path, string? OldPath)> names,
        IReadOnlyDictionary<string, (int Added, int Deleted, bool Binary)> stats) =>
        names.Select(n =>
        {
            var s = stats.TryGetValue(n.Path, out var v) ? v : (0, 0, false);
            return new ChangedFile(n.Path, n.OldPath, n.Kind, s.Item1, s.Item2, s.Item3);
        }).ToList();

    private static int Int(string s) => int.Parse(s, CultureInfo.InvariantCulture);
    private static DateTimeOffset Unix(string s) => DateTimeOffset.FromUnixTimeSeconds(long.Parse(s, CultureInfo.InvariantCulture));
}
