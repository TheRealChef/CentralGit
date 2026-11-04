namespace CentralGit.Core.Git;

public sealed record Signature(string Name, string Email, DateTimeOffset When);

public sealed record CommitInfo(
    string Hash,
    IReadOnlyList<string> Parents,
    Signature Author,
    Signature Committer,
    IReadOnlyList<string> Refs,
    string Subject)
{
    public string ShortHash => Hash.Length > 7 ? Hash[..7] : Hash;
    public bool IsMerge => Parents.Count > 1;
}

/// <summary>Reads history via <c>git log</c> with unit/record separators, so subjects can contain anything.</summary>
public static class CommitLog
{
    private const char FieldSeparator = '\u001f';
    private const char RecordSeparator = '\u001e';

    internal const string Format = "%H%x1f%P%x1f%an%x1f%ae%x1f%at%x1f%cn%x1f%ce%x1f%ct%x1f%D%x1f%s%x1e";

    public static IReadOnlyList<string> Arguments(int maxCount, bool allRefs = true, int skip = 0)
    {
        var args = new List<string> { "log", "--topo-order", "--decorate=full", $"--format={Format}" };
        if (maxCount > 0)
            args.Add($"--max-count={maxCount}");
        if (skip > 0)
            args.Add($"--skip={skip}");
        if (allRefs)
            args.Add("--all");
        return args;
    }

    public static async IAsyncEnumerable<CommitInfo> ReadAsync(
        GitRunner git,
        string repositoryPath,
        int maxCount = 0,
        int skip = 0,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var record in git.StreamRecordsAsync(repositoryPath, Arguments(maxCount, skip: skip), RecordSeparator, cancellationToken))
        {
            var commit = Parse(record);
            if (commit is not null)
                yield return commit;
        }
    }

    /// <summary>Parses one record. Returns null for blank trailing records.</summary>
    public static CommitInfo? Parse(string record)
    {
        record = record.TrimStart('\n', '\r');
        if (record.Length == 0)
            return null;

        var f = record.Split(FieldSeparator);
        if (f.Length < 10)
            throw new FormatException($"Unexpected git log record with {f.Length} fields.");

        return new CommitInfo(
            Hash: f[0],
            Parents: f[1].Length == 0 ? [] : f[1].Split(' ', StringSplitOptions.RemoveEmptyEntries),
            Author: new Signature(f[2], f[3], FromUnix(f[4])),
            Committer: new Signature(f[5], f[6], FromUnix(f[7])),
            Refs: ParseRefs(f[8]),
            // A subject may itself contain the separator in pathological cases; keep the remainder.
            Subject: string.Join(FieldSeparator, f[9..]));
    }

    /// <summary>
    /// Parses <c>%D</c> with <c>--decorate=full</c>: "HEAD -> refs/heads/main, refs/remotes/origin/main, tag: refs/tags/v1".
    /// </summary>
    internal static IReadOnlyList<string> ParseRefs(string decoration)
    {
        if (string.IsNullOrWhiteSpace(decoration))
            return [];

        var refs = new List<string>();
        foreach (var raw in decoration.Split(", ", StringSplitOptions.RemoveEmptyEntries))
        {
            var part = raw.Trim();
            if (part.StartsWith("HEAD -> ", StringComparison.Ordinal))
            {
                refs.Add("HEAD");
                part = part["HEAD -> ".Length..];
            }
            if (part.StartsWith("tag: ", StringComparison.Ordinal))
                part = part["tag: ".Length..];
            refs.Add(part);
        }
        return refs;
    }

    private static DateTimeOffset FromUnix(string seconds) =>
        DateTimeOffset.FromUnixTimeSeconds(long.Parse(seconds, System.Globalization.CultureInfo.InvariantCulture));
}
