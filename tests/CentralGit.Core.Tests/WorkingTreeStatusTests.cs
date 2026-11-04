using CentralGit.Core.Git;

namespace CentralGit.Core.Tests;

public class WorkingTreeStatusTests
{
    [Fact]
    public void Parses_branch_headers_and_all_entry_kinds()
    {
        var output = string.Join('\0',
            "# branch.oid a3f9e21aaaa",
            "# branch.head feature/invoice-pdf-renderer",
            "# branch.upstream origin/feature/invoice-pdf-renderer",
            "# branch.ab +1 -4",
            "1 MM N... 100644 100644 100644 aaaa bbbb src/InvoiceDocument.cs",
            "1 A. N... 000000 100644 100644 0000 cccc src/Page Break Policy.cs",
            "2 R. N... 100644 100644 100644 dddd dddd R100 src/New.cs",
            "src/Old.cs",
            "u UU N... 100644 100644 100644 100644 e1 e2 e3 src/Conflict.cs",
            "? notes.txt",
            "");

        var status = WorkingTreeStatus.Parse(output);

        Assert.Equal("feature/invoice-pdf-renderer", status.Branch.Head);
        Assert.Equal("origin/feature/invoice-pdf-renderer", status.Branch.Upstream);
        Assert.Equal(1, status.Branch.Ahead);
        Assert.Equal(4, status.Branch.Behind);
        Assert.Equal(5, status.Changes.Count);

        var partial = status.Changes[0];
        Assert.True(partial.IsStaged);
        Assert.True(partial.IsUnstaged);

        Assert.Equal("src/Page Break Policy.cs", status.Changes[1].Path);
        Assert.Equal(ChangeKind.Added, status.Changes[1].Staged);

        Assert.Equal("src/Old.cs", status.Changes[2].OriginalPath);
        Assert.True(status.Changes[3].IsConflicted);
        Assert.Equal(ChangeKind.Untracked, status.Changes[4].Staged);
        Assert.True(status.Changes[4].IsUnstaged);
    }

    [Fact]
    public void Detached_head_has_no_branch_name()
    {
        var status = WorkingTreeStatus.Parse("# branch.oid abc\0# branch.head (detached)\0");
        Assert.True(status.Branch.IsDetached);
    }
}
