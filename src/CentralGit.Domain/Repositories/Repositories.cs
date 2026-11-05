namespace CentralGit.Domain.Repositories;

public sealed record ScanOptions
{
    public int MaxDepth { get; init; } = 3;
    public bool IncludeNested { get; init; }
    public bool IncludeWorktrees { get; init; } = true;

    public IReadOnlySet<string> ExcludedFolderNames { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "node_modules",
            "bin",
            "obj",
            ".vs",
            "dist",
            "packages",
        };
}

public sealed record DiscoveredRepository(string Path, string Name, bool IsWorktree);

/// <summary>A repository's state at a glance: uncommitted files and ahead/behind its upstream.</summary>
public sealed record RepositoryGlance(int ChangedFiles, int Ahead, int Behind);
