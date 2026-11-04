namespace CentralGit.Core.Git;

public enum ChangeKind { Unmodified, Modified, TypeChanged, Added, Deleted, Renamed, Copied, Unmerged, Untracked, Ignored }

public sealed record FileChange(string Path, ChangeKind Staged, ChangeKind Unstaged, string? OriginalPath = null)
{
    public bool IsStaged => Staged is not (ChangeKind.Unmodified or ChangeKind.Untracked or ChangeKind.Ignored);
    public bool IsUnstaged => Unstaged is not ChangeKind.Unmodified || Staged is ChangeKind.Untracked;
    public bool IsConflicted => Staged is ChangeKind.Unmerged;
}

public sealed record BranchStatus(string? Head, string? Oid, string? Upstream, int Ahead, int Behind)
{
    public bool IsDetached => Head is null;
}

public sealed record WorkingTreeStatus(BranchStatus Branch, IReadOnlyList<FileChange> Changes)
{
    public static IReadOnlyList<string> Arguments { get; } =
        ["status", "--porcelain=v2", "--branch", "-z", "--untracked-files=all"];

    public int ChangedFileCount => Changes.Count(c => c.Staged is not ChangeKind.Ignored);

    /// <summary>Parses <c>git status --porcelain=v2 --branch -z</c>.</summary>
    public static WorkingTreeStatus Parse(string output)
    {
        string? head = null, oid = null, upstream = null;
        int ahead = 0, behind = 0;
        var changes = new List<FileChange>();

        var entries = output.Split('\0');
        for (var i = 0; i < entries.Length; i++)
        {
            var e = entries[i];
            if (e.Length == 0)
                continue;

            switch (e[0])
            {
                case '#':
                    ParseHeader(e, ref head, ref oid, ref upstream, ref ahead, ref behind);
                    break;

                case '1': // 1 XY sub mH mI mW hH hI path
                {
                    var p = e.Split(' ', 9);
                    changes.Add(new FileChange(p[8], Kind(p[1][0]), Kind(p[1][1])));
                    break;
                }

                case '2': // 2 XY sub mH mI mW hH hI Xscore path \0 origPath
                {
                    var p = e.Split(' ', 10);
                    var original = i + 1 < entries.Length ? entries[++i] : null;
                    changes.Add(new FileChange(p[9], Kind(p[1][0]), Kind(p[1][1]), original));
                    break;
                }

                case 'u': // u XY sub m1 m2 m3 mW h1 h2 h3 path
                {
                    var p = e.Split(' ', 11);
                    changes.Add(new FileChange(p[10], ChangeKind.Unmerged, ChangeKind.Unmerged));
                    break;
                }

                case '?':
                    changes.Add(new FileChange(e[2..], ChangeKind.Untracked, ChangeKind.Untracked));
                    break;

                case '!':
                    changes.Add(new FileChange(e[2..], ChangeKind.Ignored, ChangeKind.Ignored));
                    break;
            }
        }

        return new WorkingTreeStatus(new BranchStatus(head, oid, upstream, ahead, behind), changes);
    }

    private static void ParseHeader(string line, ref string? head, ref string? oid, ref string? upstream, ref int ahead, ref int behind)
    {
        var parts = line.Split(' ');
        if (parts.Length < 3)
            return;

        switch (parts[1])
        {
            case "branch.oid":
                oid = parts[2] == "(initial)" ? null : parts[2];
                break;
            case "branch.head":
                head = parts[2] == "(detached)" ? null : parts[2];
                break;
            case "branch.upstream":
                upstream = parts[2];
                break;
            case "branch.ab" when parts.Length >= 4:
                ahead = int.Parse(parts[2].TrimStart('+'), System.Globalization.CultureInfo.InvariantCulture);
                behind = int.Parse(parts[3].TrimStart('-'), System.Globalization.CultureInfo.InvariantCulture);
                break;
        }
    }

    private static ChangeKind Kind(char c) => c switch
    {
        '.' => ChangeKind.Unmodified,
        'M' => ChangeKind.Modified,
        'T' => ChangeKind.TypeChanged,
        'A' => ChangeKind.Added,
        'D' => ChangeKind.Deleted,
        'R' => ChangeKind.Renamed,
        'C' => ChangeKind.Copied,
        'U' => ChangeKind.Unmerged,
        _ => ChangeKind.Unmodified,
    };
}
