namespace CentralGit.Domain.Commits;

/// <summary>A commit message as the composer edits it: a one-line summary and an optional body.</summary>
public static class CommitMessage
{
    /// <summary>Joins a summary and an optional body the way git expects (blank line between).</summary>
    public static string Compose(string summary, string body)
    {
        // Text boxes on Windows use \r (WinUI) or \r\n for line breaks; git wants \n.
        static string Lf(string s) => s.Replace("\r\n", "\n").Replace('\r', '\n');
        summary = Lf(summary).Replace('\n', ' ').Trim();
        body = Lf(body).Trim();
        return body.Length == 0 ? summary : summary + "\n\n" + body;
    }

    /// <summary>Splits a message into summary (first paragraph, joined) and body.</summary>
    public static (string Summary, string Body) Split(string message)
    {
        message = message.Replace("\r\n", "\n").Trim();
        var gap = message.IndexOf("\n\n", StringComparison.Ordinal);
        return gap < 0
            ? (message.Replace('\n', ' '), "")
            : (message[..gap].Replace('\n', ' '), message[(gap + 2)..].Trim());
    }
}
