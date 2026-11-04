using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace CentralGit.Core.Git;

/// <summary>The outcome of one git invocation, kept verbatim so the UI can show exact diagnostics.</summary>
public sealed record GitResult(
    IReadOnlyList<string> Arguments,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration)
{
    public bool Succeeded => ExitCode == 0;

    /// <summary>The command line as a user would type it, for the Operations Center.</summary>
    public string CommandLine => "git " + string.Join(' ', Arguments.Select(Quote));

    private static string Quote(string arg) =>
        arg.Length == 0 || arg.Any(char.IsWhiteSpace) ? $"\"{arg}\"" : arg;
}

public sealed class GitException(GitResult result)
    : Exception($"{result.CommandLine} failed with exit code {result.ExitCode}: {result.StandardError.Trim()}")
{
    public GitResult Result { get; } = result;
}

/// <summary>
/// Runs the git CLI. Every call is non-interactive: credential prompts go through
/// Git Credential Manager's own UI, never a hidden terminal that would hang.
/// </summary>
public sealed class GitRunner(string gitExecutable = "git")
{
    public string GitExecutable { get; } = gitExecutable;

    public async Task<GitResult> RunAsync(
        string workingDirectory,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken = default)
    {
        var args = arguments.ToArray();
        using var process = Start(workingDirectory, args);
        var stopwatch = Stopwatch.StartNew();

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        return new GitResult(args, process.ExitCode, await stdout.ConfigureAwait(false),
            await stderr.ConfigureAwait(false), stopwatch.Elapsed);
    }

    /// <summary>Runs git and throws <see cref="GitException"/> on a non-zero exit code.</summary>
    public async Task<string> RunCheckedAsync(
        string workingDirectory,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(workingDirectory, arguments, cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? result.StandardOutput : throw new GitException(result);
    }

    /// <summary>
    /// Streams stdout split on <paramref name="separator"/>, so large outputs
    /// (git log over 100k commits) never have to sit in memory as one string.
    /// </summary>
    public async IAsyncEnumerable<string> StreamRecordsAsync(
        string workingDirectory,
        IEnumerable<string> arguments,
        char separator,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var args = arguments.ToArray();
        using var process = Start(workingDirectory, args);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var registration = cancellationToken.Register(() => TryKill(process));

        var buffer = new char[16 * 1024];
        var record = new StringBuilder();
        int read;
        while ((read = await process.StandardOutput.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                if (buffer[i] == separator)
                {
                    yield return record.ToString();
                    record.Clear();
                }
                else
                {
                    record.Append(buffer[i]);
                }
            }
        }

        if (record.Length > 0)
            yield return record.ToString();

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new GitException(new GitResult(args, process.ExitCode, string.Empty, await stderr.ConfigureAwait(false), TimeSpan.Zero));
    }

    private Process Start(string workingDirectory, string[] args)
    {
        var info = new ProcessStartInfo(GitExecutable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        // Stable, parseable output regardless of user config.
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add("core.quotePath=false");
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add("color.ui=false");
        foreach (var arg in args)
            info.ArgumentList.Add(arg);

        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        info.Environment["GIT_OPTIONAL_LOCKS"] = "0"; // read-only status must not fight the user's own git

        return Process.Start(info) ?? throw new InvalidOperationException($"Could not start {GitExecutable}.");
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // already exited
        }
    }
}
