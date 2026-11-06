using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using CentralGit.Application.Ports;
using CentralGit.Domain.Hosting;
using CentralGit.Infrastructure.Git;

namespace CentralGit.Infrastructure.Hosting;

/// <summary>
/// GitHub's REST API. The token is whatever Git Credential Manager already holds for github.com,
/// asked for without any sign-in prompt; without one, requests go out anonymously (public
/// repositories only, low rate limit). Nothing is stored here, and errors read as "unknown".
/// </summary>
public sealed class GitHubHosting(GitRunner git) : IHostingProvider, IDisposable
{
    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private readonly HttpClient _http = CreateClient();
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _token;
    private bool _tokenLooked;

    public async Task<PullRequest?> FindOpenPullRequestAsync(
        HostedRepository repository,
        string branch,
        CancellationToken ct = default
    )
    {
        var url =
            $"https://api.github.com/repos/{repository.Owner}/{repository.Name}/pulls"
            + $"?state=open&per_page=5&head={Uri.EscapeDataString($"{repository.Owner}:{branch}")}";
        using var json = await GetAsync(url, ct).ConfigureAwait(false);
        if (json is null || json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() == 0)
            return null;
        var pr = json.RootElement[0];
        return new PullRequest(
            pr.GetProperty("number").GetInt32(),
            pr.GetProperty("title").GetString() ?? "",
            pr.GetProperty("html_url").GetString() ?? repository.WebUrl,
            pr.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True,
            pr.GetProperty("user").GetProperty("login").GetString() ?? "",
            pr.GetProperty("base").GetProperty("ref").GetString() ?? ""
        );
    }

    public async Task<CheckState> GetChecksAsync(
        HostedRepository repository,
        string commit,
        CancellationToken ct = default
    )
    {
        var url =
            $"https://api.github.com/repos/{repository.Owner}/{repository.Name}/commits/{commit}/check-runs?per_page=100";
        using var json = await GetAsync(url, ct).ConfigureAwait(false);
        if (json is null || !json.RootElement.TryGetProperty("check_runs", out var runs))
            return CheckState.None;
        return Checks.Combine(
            runs.EnumerateArray()
                .Select(r => new CheckRun(
                    r.GetProperty("name").GetString() ?? "",
                    r.GetProperty("status").GetString() ?? "",
                    r.TryGetProperty("conclusion", out var c) && c.ValueKind == JsonValueKind.String
                        ? c.GetString()
                        : null
                ))
        );
    }

    private async Task<JsonDocument?> GetAsync(string url, CancellationToken ct)
    {
        try
        {
            var token = await TokenAsync(ct).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (token is not null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized && token is not null)
            {
                // A stale token: forget it and ask GCM again next time.
                _tokenLooked = false;
                _token = null;
                return null;
            }
            if (!response.IsSuccessStatusCode)
                return null;
            var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception e)
            when (e is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>GCM's stored github.com token, looked up once, never with a prompt.</summary>
    private async Task<string?> TokenAsync(CancellationToken ct)
    {
        if (_tokenLooked)
            return _token;
        await _tokenLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_tokenLooked)
                return _token;
            var fill = await git.RunAsync(
                    Home,
                    ["-c", "credential.interactive=false", "credential", "fill"],
                    ct,
                    "protocol=https\nhost=github.com\n\n"
                )
                .ConfigureAwait(false);
            _token = fill.Succeeded
                ? fill
                    .StandardOutput.Split('\n')
                    .Select(l => l.TrimEnd('\r'))
                    .FirstOrDefault(l => l.StartsWith("password=", StringComparison.Ordinal))
                    ?[9..]
                : null;
            _tokenLooked = true;
            return _token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CentralGit", "1.0"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return http;
    }

    public void Dispose()
    {
        _http.Dispose();
        _tokenLock.Dispose();
    }
}
