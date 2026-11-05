using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using CentralGit.Application.Git;

namespace CentralGit.Infrastructure.Git;

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
        CancellationToken cancellationToken = default,
        string? standardInput = null
    )
    {
        var args = arguments.ToArray();
        using var process = Start(workingDirectory, args, redirectInput: standardInput is not null);
        var stopwatch = Stopwatch.StartNew();

        if (standardInput is not null)
        {
            // git credential & co. read until EOF.
            await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken).ConfigureAwait(false);
            process.StandardInput.Close();
        }

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

        return new GitResult(
            args,
            process.ExitCode,
            await stdout.ConfigureAwait(false),
            await stderr.ConfigureAwait(false),
            stopwatch.Elapsed
        );
    }

    /// <summary>
    /// Runs a long operation (fetch, pull, push) and reports each stderr progress line as it
    /// appears. Git redraws progress with carriage returns, so both \r and \n end a line.
    /// The full stderr is still returned in the result for diagnostics.
    /// </summary>
    public async Task<GitResult> RunWithProgressAsync(
        string workingDirectory,
        IEnumerable<string> arguments,
        Action<string> onProgress,
        CancellationToken cancellationToken = default
    )
    {
        var args = arguments.ToArray();
        using var process = Start(workingDirectory, args);
        var stopwatch = Stopwatch.StartNew();
        using var registration = cancellationToken.Register(() => TryKill(process));

        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var all = new StringBuilder();
        var line = new StringBuilder();
        var buffer = new char[4096];
        int read;
        while ((read = await process.StandardError.ReadAsync(buffer, CancellationToken.None).ConfigureAwait(false)) > 0)
        {
            all.Append(buffer, 0, read);
            for (var i = 0; i < read; i++)
            {
                if (buffer[i] is '\r' or '\n')
                {
                    if (line.Length > 0)
                        onProgress(line.ToString());
                    line.Clear();
                }
                else
                {
                    line.Append(buffer[i]);
                }
            }
        }
        if (line.Length > 0)
            onProgress(line.ToString());

        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        // Keep only the final state of each \r-redrawn progress line, so the log reads cleanly.
        var cleaned = string.Join(
            '\n',
            all.ToString()
                .Replace("\r\n", "\n")
                .Split('\n')
                .Select(l => l.Split('\r').LastOrDefault(s => s.Length > 0) ?? "")
        );
        return new GitResult(args, process.ExitCode, await stdout.ConfigureAwait(false), cleaned, stopwatch.Elapsed);
    }

    /// <summary>Runs git and throws <see cref="GitException"/> on a non-zero exit code.</summary>
    public async Task<string> RunCheckedAsync(
        string workingDirectory,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken = default,
        string? standardInput = null
    )
    {
        var result = await RunAsync(workingDirectory, arguments, cancellationToken, standardInput)
            .ConfigureAwait(false);
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
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
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
            throw new GitException(
                new GitResult(args, process.ExitCode, string.Empty, await stderr.ConfigureAwait(false), TimeSpan.Zero)
            );
    }

    private Process Start(string workingDirectory, string[] args, bool redirectInput = false)
    {
        var info = new ProcessStartInfo(GitExecutable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = redirectInput,
            StandardInputEncoding = redirectInput ? new UTF8Encoding(false) : null, // no BOM: git would read it as data
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

        // Never block on a hidden terminal prompt. Git Credential Manager still shows its own
        // sign-in window when a remote needs credentials (it only uses this for terminal prompts).
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
