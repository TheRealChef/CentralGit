using CentralGit.Domain.Changes;

namespace CentralGit.Infrastructure.FileSystem;

/// <summary>Counts lines in a file git has no diff for (untracked files).</summary>
internal static class LineCounter
{
    /// <summary>Small text files only; binary → <see cref="LineCounts.Binary"/>, huge or unreadable → null.</summary>
    public static LineCounts? Count(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 4 * 1024 * 1024)
                return null;
            var bytes = File.ReadAllBytes(path);
            if (Array.IndexOf(bytes, (byte)0, 0, Math.Min(bytes.Length, 8000)) >= 0)
                return LineCounts.Binary;
            var lines = bytes.Count(b => b == (byte)'\n');
            if (bytes.Length > 0 && bytes[^1] != (byte)'\n')
                lines++;
            return new LineCounts(lines, 0);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
