using CentralGit.Application.Features.Changes;
using CentralGit.Application.Features.Workflow;
using CentralGit.Application.Operations;
using CentralGit.Domain.Changes;
using CentralGit.Domain.Commits;
using CentralGit.Domain.Operations;
using CentralGit.Domain.Workflow;
using CentralGit.Infrastructure.Git;

namespace CentralGit.Infrastructure.Tests;

/// <summary>Commit actions, merge/rebase with conflicts, tags, stash, blame and line staging against real git.</summary>
public sealed class WorkflowTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("centralgit-workflow-").FullName;
    private readonly GitRunner _git = new();
    private readonly GitRepository _repo;

    public WorkflowTests() => _repo = new GitRepository(_root, _git);

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal); // git marks objects read-only
        Directory.Delete(_root, recursive: true);
    }

    private Task<string> Git(params string[] args) => _git.RunCheckedAsync(_root, args);

    private void Write(string file, string text) => File.WriteAllText(Path.Combine(_root, file), text);

    private string Read(string file) => File.ReadAllText(Path.Combine(_root, file)).Replace("\r\n", "\n");

    private async Task InitAsync()
    {
        await Git("init", "-q", "-b", "main");
        await Git("config", "user.name", "Test");
        await Git("config", "user.email", "test@example.dev");
        await Git("config", "commit.gpgsign", "false");
        await Git("config", "core.autocrlf", "false");
        Write("a.txt", "one\ntwo\nthree\n");
        await Git("add", ".");
        await Git("commit", "-qm", "First");
    }

    private static async Task<OperationOutcome> RunAsync(IPreparedOperation operation) =>
        await operation.RunAsync(_ => { }, CancellationToken.None);

    private async Task<CommitInfo> CommitAsync(string rev) =>
        (await _repo.GetHistoryAsync(50)).First(c => c.Hash == (Git("rev-parse", rev).Result.Trim()));

    [Fact]
    public async Task Cherry_pick_and_revert_make_new_commits()
    {
        await InitAsync();
        await Git("switch", "-qc", "topic");
        Write("b.txt", "topic\n");
        await Git("add", ".");
        await Git("commit", "-qm", "Add b");
        await Git("switch", "-q", "main");

        var actions = new CommitActionService(_repo, _repo);
        Assert.True((await RunAsync(actions.CherryPick(await CommitAsync("topic")))).Succeeded);
        Assert.Equal("topic\n", Read("b.txt"));

        var revert = await RunAsync(actions.Revert(await CommitAsync("HEAD")));
        Assert.True(revert.Succeeded, revert.Result.StandardError);
        Assert.False(File.Exists(Path.Combine(_root, "b.txt")));
        Assert.StartsWith("Revert", (await Git("log", "-1", "--format=%s")).Trim());
    }

    [Fact]
    public async Task Soft_reset_keeps_the_changes_staged()
    {
        await InitAsync();
        Write("a.txt", "changed\n");
        await Git("commit", "-qam", "Second");

        var outcome = await RunAsync(new CommitActionService(_repo, _repo).Reset("HEAD~1", ResetMode.Soft));
        Assert.True(outcome.Succeeded);
        Assert.Equal("First", (await Git("log", "-1", "--format=%s")).Trim());
        Assert.Contains(
            (await _repo.GetStatusAsync()).Changes,
            c => c.Path == "a.txt" && c.Staged == ChangeKind.Modified
        );
    }

    [Fact]
    public async Task A_conflicting_merge_stops_is_explained_and_resolves_then_continues()
    {
        await InitAsync();
        await Git("switch", "-qc", "topic");
        Write("a.txt", "one\nTOPIC\nthree\n");
        await Git("commit", "-qam", "Topic edit");
        await Git("switch", "-q", "main");
        Write("a.txt", "one\nMAIN\nthree\n");
        await Git("commit", "-qam", "Main edit");

        var actions = new CommitActionService(_repo, _repo);
        var merge = await RunAsync(actions.Merge("topic"));
        Assert.False(merge.Succeeded);
        Assert.Equal(SuggestedFix.ResolveConflicts, merge.Failure!.Fix);
        Assert.Equal(InProgress.Merge, await actions.GetInProgressAsync());
        Assert.Contains((await _repo.GetStatusAsync()).Changes, c => c.Path == "a.txt" && c.IsConflicted);

        Assert.True((await actions.ResolveConflictAsync("a.txt", ConflictSide.Theirs)).Succeeded);
        Assert.Equal("one\nTOPIC\nthree\n", Read("a.txt"));
        var done = await RunAsync(actions.Continue(InProgress.Merge));
        Assert.True(done.Succeeded, done.Result.StandardError);
        Assert.Equal(InProgress.None, await actions.GetInProgressAsync());
        Assert.Equal(2, (await Git("log", "-1", "--format=%p")).Trim().Split(' ').Length);
    }

    [Fact]
    public async Task A_conflict_is_resolved_block_by_block_from_the_editor()
    {
        await InitAsync();
        await Git("switch", "-qc", "topic");
        Write("a.txt", "one\nTOPIC\nthree\n");
        await Git("commit", "-qam", "Topic edit");
        await Git("switch", "-q", "main");
        Write("a.txt", "one\nMAIN\nthree\n");
        await Git("commit", "-qam", "Main edit");
        var actions = new CommitActionService(_repo, _repo);
        Assert.False((await RunAsync(actions.Merge("topic"))).Succeeded);

        var text = await actions.ReadConflictAsync("a.txt");
        Assert.NotNull(text);
        var conflict = Assert.Single(text.Conflicts);
        Assert.Equal(("MAIN\n", "TOPIC\n"), (conflict.Ours.Replace("\r", ""), conflict.Theirs.Replace("\r", "")));

        var saved = await actions.SaveResolutionAsync("a.txt", text, [ConflictChoice.OursThenTheirs]);
        Assert.True(saved.Succeeded, saved.StandardError);
        Assert.Equal("one\nMAIN\nTOPIC\nthree\n", Read("a.txt"));
        Assert.DoesNotContain((await _repo.GetStatusAsync()).Changes, c => c.IsConflicted);
    }

    [Fact]
    public async Task Interactive_rebase_reorders_squashes_rewords_and_drops_without_an_editor()
    {
        await InitAsync(); // "First" with a.txt
        foreach (
            var (file, subject) in new[]
            {
                ("b.txt", "Add b"),
                ("c.txt", "Add c"),
                ("d.txt", "Add d"),
                ("e.txt", "Add e"),
            }
        )
        {
            Write(file, subject + "\n");
            await Git("add", file);
            await Git("commit", "-qm", subject);
        }
        var actions = new CommitActionService(_repo, _repo);
        var b = (await Git("rev-parse", "HEAD~3")).Trim();

        var range = await actions.GetRebaseRangeAsync(b);
        Assert.NotNull(range.Base);
        Assert.False(range.HasMerges);
        Assert.Equal(["Add b", "Add c", "Add d", "Add e"], range.Steps.Select(s => s.Subject));

        // e first (reworded), then b with c squashed in, d dropped.
        var s = range.Steps;
        RebaseStep[] plan =
        [
            s[3] with
            {
                Action = RebaseAction.Reword,
                Message = "Add e, it's 'quoted'",
            },
            s[0],
            s[1] with
            {
                Action = RebaseAction.Fixup,
            },
            s[2] with
            {
                Action = RebaseAction.Drop,
            },
        ];
        Assert.Null(RebasePlan.Check(plan, [.. s.Select(x => x.Hash)]));
        var outcome = await RunAsync(actions.InteractiveRebase(range, plan));
        Assert.True(outcome.Succeeded, outcome.Result.StandardError);

        Assert.Equal(["Add b", "Add e, it's 'quoted'", "First"], (await Git("log", "--format=%s")).Trim().Split('\n'));
        Assert.False(File.Exists(Path.Combine(_root, "d.txt")));
        Assert.True(File.Exists(Path.Combine(_root, "c.txt")));
        Assert.Equal(InProgress.None, await actions.GetInProgressAsync());
    }

    [Fact]
    public async Task Bisect_narrows_down_to_the_first_bad_commit()
    {
        await InitAsync();
        var hashes = new List<string>();
        for (var i = 1; i <= 8; i++)
        {
            Write("n.txt", i >= 6 ? $"broken {i}\n" : $"ok {i}\n");
            await Git("add", "n.txt");
            await Git("commit", "-qm", $"Step {i}");
            hashes.Add((await Git("rev-parse", "HEAD")).Trim());
        }
        var actions = new CommitActionService(_repo, _repo);
        Assert.False((await actions.GetBisectAsync()).IsActive);

        Assert.True((await RunAsync(actions.BisectStart())).Succeeded);
        Assert.True((await RunAsync(actions.BisectMark(hashes[^1], BisectMark.Bad))).Succeeded);
        Assert.True((await RunAsync(actions.BisectMark(hashes[0], BisectMark.Good))).Succeeded);

        // Answer git's questions by looking at the file, like a person running the test would.
        var state = await actions.GetBisectAsync();
        for (var guard = 0; state.FirstBad is null && guard < 10; guard++)
        {
            Assert.True(state.StepsLeft > 0);
            var mark = Read("n.txt").StartsWith("broken", StringComparison.Ordinal) ? BisectMark.Bad : BisectMark.Good;
            Assert.True((await RunAsync(actions.BisectMark(null, mark))).Succeeded);
            state = await actions.GetBisectAsync();
        }
        Assert.Equal(hashes[5], state.FirstBad);

        Assert.True((await RunAsync(actions.BisectReset())).Succeeded);
        Assert.False((await actions.GetBisectAsync()).IsActive);
    }

    [Fact]
    public async Task The_commit_graph_is_written_once_then_left_alone_while_fresh()
    {
        await InitAsync();
        Assert.True(await _repo.RefreshCommitGraphAsync(TimeSpan.FromDays(1)));
        Assert.False(await _repo.RefreshCommitGraphAsync(TimeSpan.FromDays(1)));
        Assert.True(await _repo.RefreshCommitGraphAsync(TimeSpan.Zero));
        Assert.Equal("First", (await Git("log", "-1", "--format=%s")).Trim());
    }

    [Fact]
    public async Task Health_sees_submodules_and_lfs_pointer_files()
    {
        await InitAsync();
        var library = Path.Combine(_root, "_library");
        Directory.CreateDirectory(library);
        await _git.RunCheckedAsync(library, ["init", "-q", "-b", "main"]);
        await _git.RunCheckedAsync(
            library,
            ["-c", "user.name=T", "-c", "user.email=t@t", "commit", "-q", "--allow-empty", "-m", "lib"]
        );
        await Git("-c", "protocol.file.allow=always", "submodule", "add", "-q", library.Replace('\\', '/'), "lib");
        Write(".gitattributes", "*.bin filter=lfs diff=lfs merge=lfs -text\n");
        Write(
            "big.bin",
            "version https://git-lfs.github.com/spec/v1\noid sha256:" + new string('a', 64) + "\nsize 10\n"
        );
        await Git("add", ".gitattributes", "big.bin");
        await Git("commit", "-qm", "Submodule and LFS");

        var facts = await _repo.ReadFactsAsync(DateTimeOffset.Now);
        var sub = Assert.Single(facts.Submodules!);
        Assert.Equal(("lib", Domain.Health.SubmoduleState.UpToDate), (sub.Path, sub.State));
        Assert.Equal((1, 1), (facts.Lfs!.Files, facts.Lfs.NotDownloaded));

        await _git.RunCheckedAsync(
            Path.Combine(_root, "lib"),
            ["-c", "user.name=T", "-c", "user.email=t@t", "commit", "-q", "--allow-empty", "-m", "moved"]
        );
        facts = await _repo.ReadFactsAsync(DateTimeOffset.Now);
        Assert.Equal(Domain.Health.SubmoduleState.DifferentCommit, facts.Submodules![0].State);
    }

    [Fact]
    public async Task A_conflicting_rebase_can_be_aborted()
    {
        await InitAsync();
        await Git("switch", "-qc", "topic");
        Write("a.txt", "one\nTOPIC\nthree\n");
        await Git("commit", "-qam", "Topic edit");
        await Git("switch", "-q", "main");
        Write("a.txt", "one\nMAIN\nthree\n");
        await Git("commit", "-qam", "Main edit");
        await Git("switch", "-q", "topic");
        var before = (await Git("rev-parse", "HEAD")).Trim();

        var actions = new CommitActionService(_repo, _repo);
        var rebase = await RunAsync(actions.Rebase("main"));
        Assert.False(rebase.Succeeded);
        Assert.Equal(InProgress.Rebase, await actions.GetInProgressAsync());

        Assert.True((await RunAsync(actions.Abort(InProgress.Rebase))).Succeeded);
        Assert.Equal(InProgress.None, await actions.GetInProgressAsync());
        Assert.Equal(before, (await Git("rev-parse", "HEAD")).Trim());
    }

    [Fact]
    public async Task Tags_are_created_listed_and_deleted()
    {
        await InitAsync();
        var head = (await Git("rev-parse", "HEAD")).Trim();
        var tags = new TagService(_repo, _repo);

        Assert.Null(await tags.ValidateNewNameAsync("v1.0"));
        Assert.NotNull(await tags.ValidateNewNameAsync("bad..name"));
        Assert.True((await RunAsync(tags.Create("v1.0", head, "First release"))).Succeeded);
        Assert.True((await RunAsync(tags.Create("light", head, null))).Succeeded);
        Assert.Equal("A tag called v1.0 already exists.", await tags.ValidateNewNameAsync("v1.0"));

        var list = await tags.GetTagsAsync();
        var v1 = Assert.Single(list, t => t.Name == "v1.0");
        Assert.Equal((head, true, "First release"), (v1.Commit, v1.IsAnnotated, v1.Message));
        var light = Assert.Single(list, t => t.Name == "light");
        Assert.Equal((head, false), (light.Commit, light.IsAnnotated));

        Assert.True((await RunAsync(tags.Delete("light"))).Succeeded);
        Assert.DoesNotContain(await tags.GetTagsAsync(), t => t.Name == "light");
    }

    [Fact]
    public async Task Stash_saves_lists_shows_files_and_pops()
    {
        await InitAsync();
        Write("a.txt", "one\nTWO\nthree\n");
        Write("new.txt", "brand new\n");
        var stashes = new StashService(_repo, _repo, _repo);

        Assert.True((await RunAsync(stashes.Save("halfway", includeUntracked: true, []))).Succeeded);
        var stash = Assert.Single(await stashes.GetStashesAsync());
        Assert.Equal(("main", "halfway"), (stash.Branch, stash.Message));
        Assert.Equal("one\ntwo\nthree\n", Read("a.txt"));

        var files = await stashes.GetFilesAsync(stash);
        Assert.Equal(["a.txt", "new.txt"], files.Select(f => f.File.Path));
        Assert.True(files.Single(f => f.File.Path == "new.txt").IsUntracked);
        var diff = await stashes.GetFileDiffAsync(files[0]);
        Assert.Contains(diff.Hunks.SelectMany(h => h.Lines), l => l.Text == "TWO");

        Assert.True((await RunAsync(stashes.Pop(stash))).Succeeded);
        Assert.Empty(await stashes.GetStashesAsync());
        Assert.Equal("brand new\n", Read("new.txt"));
    }

    [Fact]
    public async Task Stashing_selected_files_leaves_the_others()
    {
        await InitAsync();
        Write("b.txt", "b\n");
        await Git("add", ".");
        await Git("commit", "-qm", "Add b");
        Write("a.txt", "A\n");
        Write("b.txt", "B\n");

        var outcome = await RunAsync(new StashService(_repo, _repo, _repo).Save("", false, ["a.txt"]));
        Assert.True(outcome.Succeeded, outcome.Result.StandardError);
        Assert.Equal("one\ntwo\nthree\n", Read("a.txt"));
        Assert.Equal("B\n", Read("b.txt"));
    }

    [Fact]
    public async Task Blame_names_the_commit_of_each_line_and_marks_uncommitted_ones()
    {
        await InitAsync();
        Write("a.txt", "one\nTWO\nthree\n");
        await Git("commit", "-qam", "Shout two");
        Write("a.txt", "one\nTWO\nthree\nfour\n");

        var lines = await new BlameService(_repo).BlameAsync("a.txt", null);
        Assert.Equal([1, 2, 3, 4], lines.Select(l => l.Number));
        Assert.Equal("First", lines[0].Summary);
        Assert.Equal("Shout two", lines[1].Summary);
        Assert.Equal(lines[0].Commit, lines[2].Commit);
        Assert.True(lines[3].IsUncommitted);

        var committed = await new BlameService(_repo).BlameAsync("a.txt", "HEAD");
        Assert.Equal(3, committed.Count);
    }

    [Fact]
    public async Task Single_lines_can_be_staged_unstaged_and_discarded()
    {
        await InitAsync();
        Write("a.txt", "one\nTWO\nthree\nfour\n");
        var changes = new ChangesService(_repo);
        var change = (await _repo.GetStatusAsync()).Changes.Single();
        var diff = await _repo.DiffAsync(change, ChangeSide.Unstaged);
        var lines = diff.Hunks[0].Lines;
        int Index(DiffLineKindText k) => lines.ToList().FindIndex(l => l.Kind == k.Kind && l.Text == k.Text);

        // Stage only the added "four".
        var four = Index(new(Domain.Diff.DiffLineKind.Added, "four"));
        Assert.True(
            (await changes.ApplyLinesToIndexAsync(diff, 0, new HashSet<int> { four }, ChangeSide.Unstaged)).Succeeded
        );
        Assert.Equal("one\ntwo\nthree\nfour\n", (await Git("show", ":a.txt")).Replace("\r\n", "\n"));

        // Unstage it again.
        var staged = await _repo.DiffAsync(change, ChangeSide.Staged);
        var stagedFour = staged.Hunks[0].Lines.ToList().FindIndex(l => l.Text == "four");
        Assert.True(
            (
                await changes.ApplyLinesToIndexAsync(staged, 0, new HashSet<int> { stagedFour }, ChangeSide.Staged)
            ).Succeeded
        );
        Assert.Equal("one\ntwo\nthree\n", (await Git("show", ":a.txt")).Replace("\r\n", "\n"));

        // Discard only the "two" → "TWO" edit; "four" stays in the working tree.
        diff = await _repo.DiffAsync(change, ChangeSide.Unstaged);
        lines = diff.Hunks[0].Lines;
        var edit = new HashSet<int>
        {
            Index(new(Domain.Diff.DiffLineKind.Deleted, "two")),
            Index(new(Domain.Diff.DiffLineKind.Added, "TWO")),
        };
        Assert.True((await changes.DiscardLinesAsync(diff, 0, edit)).Succeeded);
        Assert.Equal("one\ntwo\nthree\nfour\n", Read("a.txt"));
    }

    private readonly record struct DiffLineKindText(Domain.Diff.DiffLineKind Kind, string Text);
}
