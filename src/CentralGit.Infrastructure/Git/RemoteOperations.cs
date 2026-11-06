using System.Globalization;
using System.Text.RegularExpressions;
using CentralGit.Application.Git;
using CentralGit.Domain.Operations;

namespace CentralGit.Infrastructure.Git;

/// <summary>Command lines for fetch, pull and push; reading their progress and explaining their failures.</summary>
public static partial class RemoteOperations
{
    public static IReadOnlyList<string> Fetch() => ["fetch", "--all", "--prune", "--progress"];

    public static IReadOnlyList<string> Pull(PullMode mode) =>
        mode switch
        {
            PullMode.Rebase => ["pull", "--rebase", "--progress"],
            PullMode.Merge => ["pull", "--no-rebase", "--progress"],
            _ => ["pull", "--ff-only", "--progress"],
        };

    /// <summary>
    /// Push the current branch. Without an upstream it is published to <paramref name="remote"/>
    /// and tracked from then on. Force is always --force-with-lease: it refuses to overwrite
    /// commits on the remote that we have not seen.
    /// </summary>
    public static IReadOnlyList<string> Push(
        string branch,
        bool hasUpstream,
        string remote,
        bool forceWithLease = false
    )
    {
        List<string> args = ["push", "--progress"];
        if (forceWithLease)
            args.Add("--force-with-lease");
        if (!hasUpstream)
            args.AddRange(["-u", remote, branch]);
        return args;
    }

    [GeneratedRegex(@"^(?:remote:\s*)?(?<phase>[A-Za-z][A-Za-z ]+?):\s+(?<pct>\d{1,3})%")]
    private static partial Regex ProgressLine();

    /// <summary>"Receiving objects:  45% (450/1000)" → ("Receiving objects", 45). Other lines keep their text.</summary>
    public static GitProgress ParseProgress(string line)
    {
        var m = ProgressLine().Match(line.Trim());
        if (m.Success)
            return new GitProgress(
                m.Groups["phase"].Value,
                int.Parse(m.Groups["pct"].Value, CultureInfo.InvariantCulture)
            );
        var text = line.Trim();
        if (text.StartsWith("remote: ", StringComparison.Ordinal))
            text = text[8..].Trim();
        return new GitProgress(text, null);
    }

    /// <summary>Turns git's stderr into a sentence a person can act on. Never hides the raw output; this is a summary.</summary>
    public static OperationExplanation Explain(GitResult result)
    {
        var err = result.StandardError;
        // Conflicts and some refusals are reported on stdout (merge, stash apply), the rest on stderr.
        var all = err + "\n" + result.StandardOutput;
        bool Has(string s) => all.Contains(s, StringComparison.OrdinalIgnoreCase);

        if (Has("[rejected]") && (Has("fetch first") || Has("non-fast-forward")))
            return new(
                "The remote has commits you don't have",
                "Someone pushed to this branch since you last pulled. Pull first, then push again.",
                SuggestedFix.PullFirst
            );
        if (Has("[rejected]") && Has("stale info"))
            return new(
                "The remote changed since you last fetched",
                "Force-with-lease refused to overwrite commits you haven't seen. Fetch and look at them first.",
                SuggestedFix.PullFirst
            );
        if (Has("Need to specify how to reconcile divergent branches") || Has("Not possible to fast-forward"))
            return new(
                "Your branch and the remote have both moved on",
                "Choose how to combine them: rebase your commits on top of the remote, or merge the two.",
                SuggestedFix.ChooseReconcile
            );
        if (Has("has no upstream branch"))
            return new(
                "This branch isn't on the remote yet",
                "Publish it to create the remote branch and track it.",
                SuggestedFix.PublishBranch
            );
        if (Has("There is no tracking information"))
            return new(
                "This branch doesn't track a remote branch",
                "Publish it first, or set its upstream.",
                SuggestedFix.PublishBranch
            );
        if (
            Has("Authentication failed")
            || Has("could not read Username")
            || Has("terminal prompts disabled")
            || Has("returned error: 401")
            || Has("returned error: 403")
            || Has("Permission denied (publickey)")
        )
            return new(
                "Sign-in failed",
                "The remote rejected your credentials, or none are stored. Sign in under Settings › Accounts.",
                SuggestedFix.SignIn
            );
        if (
            Has("would be overwritten")
            || Has("Please commit your changes or stash them")
            || Has("cannot pull with rebase: You have unstaged changes")
            || Has("cannot rebase: You have unstaged changes")
            || Has("cannot rebase: Your index contains uncommitted changes")
            || Has("Your local changes to the following files would be overwritten")
        )
            return new(
                "Your uncommitted changes are in the way",
                "Commit or stash them, then try again.",
                SuggestedFix.CommitOrStash
            );
        if (Has("is not fully merged"))
            return new(
                "This branch has commits that aren't merged anywhere",
                "Deleting it would leave those commits unreachable (they stay recoverable for a while through the reflog).",
                SuggestedFix.ForceDelete
            );
        if (Has("already exists"))
            return new(
                "That name is taken",
                FirstErrorLine(err) ?? "A branch with this name already exists.",
                SuggestedFix.PickAnotherName
            );
        if (Has("CONFLICT") || Has("Automatic merge failed") || Has("could not apply"))
            return new(
                "Stopped on conflicts",
                "Some files changed on both sides. Resolve them, then continue — or abort to go back.",
                SuggestedFix.ResolveConflicts
            );
        if (Has("previous cherry-pick is now empty") || Has("nothing to commit, working tree clean"))
            return new(
                "Nothing left to apply",
                "This change is already on the branch, so there's nothing to commit. Abort to finish.",
                SuggestedFix.None
            );
        if (Has("Not a valid object name") || Has("unknown revision") || Has("is not a valid"))
            return new(
                "Git doesn't know that name",
                FirstErrorLine(err) ?? "The branch, tag or commit couldn't be found.",
                SuggestedFix.None
            );
        if (
            Has("Could not resolve host")
            || Has("unable to access")
            || Has("Connection timed out")
            || Has("Could not read from remote repository")
        )
            return new(
                "Couldn't reach the remote",
                FirstErrorLine(err) ?? "Check your network or VPN.",
                SuggestedFix.CheckConnection
            );

        return new(
            "Git reported an error",
            FirstErrorLine(err) ?? $"git exited with code {result.ExitCode}.",
            SuggestedFix.None
        );
    }

    private static string? FirstErrorLine(string err) =>
        err.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(l =>
                l.StartsWith("fatal:", StringComparison.Ordinal) || l.StartsWith("error:", StringComparison.Ordinal)
            )
        ?? err.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
}
