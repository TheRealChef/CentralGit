using System.Globalization;
using CentralGit.App.Theming;
using CentralGit.Core.Git;
using Windows.UI;

namespace CentralGit.App.ViewModels;

/// <summary>Immutable presentation of one commit for the inspector; built off the UI thread.</summary>
public sealed class CommitDetailsViewModel
{
    public CommitDetailsViewModel(CommitDetails d)
    {
        Model = d;
        Subject = d.Subject;
        Body = d.Body;
        HasBody = d.Body.Length > 0;
        AuthorName = d.Author.Name;
        AuthorEmail = d.Author.Email;
        AuthorInitials = Initials(d.Author.Name);
        AuthorDate = $"{FormatDate(d.Author.When)} · {RelativeTime.Format(d.Author.When, DateTimeOffset.Now)}";

        var sameCommitter = d.Committer.Email.Equals(d.Author.Email, StringComparison.OrdinalIgnoreCase)
                            && Math.Abs((d.Committer.When - d.Author.When).TotalMinutes) < 1;
        CommitterLine = sameCommitter ? "" : $"Committed by {d.Committer.Name} · {FormatDate(d.Committer.When)}";
        HasCommitter = !sameCommitter;

        Hash = d.Hash;
        ShortHash = d.Hash[..Math.Min(10, d.Hash.Length)];
        Parents = d.Parents.Select(p => new ParentViewModel(p)).ToList();
        ParentsLabel = d.Parents.Count switch { 0 => "Root commit", 1 => "Parent", _ => $"Merge of {d.Parents.Count} parents · diff vs first" };

        (SignatureText, SignatureColor) = d.Signature switch
        {
            SignatureState.Good => ($"Verified signature{Signer(d)}", Palette.GreenText),
            SignatureState.GoodUnknownValidity => ($"Signed, key not trusted{Signer(d)}", Palette.OrangeText),
            SignatureState.Bad => ("Bad signature", Palette.RedText),
            SignatureState.ExpiredSignature or SignatureState.ExpiredKey => ($"Signed with an expired key{Signer(d)}", Palette.OrangeText),
            SignatureState.RevokedKey => ($"Signed with a revoked key{Signer(d)}", Palette.RedText),
            SignatureState.CannotCheck => ("Signed — cannot verify (key missing)", Palette.TextMuted),
            _ => ("Not signed", Palette.TextMuted),
        };

        Files = d.Files.Select(f => new ChangedFileViewModel(f)).ToList();
        FilesSummary = d.Files.Count == 1 ? "1 file" : $"{d.Files.Count} files";
        AddedText = $"+{d.TotalAdded:N0}";
        DeletedText = $"−{d.TotalDeleted:N0}";
    }

    public CommitDetails Model { get; }
    public string Subject { get; }
    public string Body { get; }
    public bool HasBody { get; }
    public string AuthorName { get; }
    public string AuthorEmail { get; }
    public string AuthorInitials { get; }
    public string AuthorDate { get; }
    public string CommitterLine { get; }
    public bool HasCommitter { get; }
    public string Hash { get; }
    public string ShortHash { get; }
    public IReadOnlyList<ParentViewModel> Parents { get; }
    public string ParentsLabel { get; }
    public string SignatureText { get; }
    public Color SignatureColor { get; }
    public IReadOnlyList<ChangedFileViewModel> Files { get; }
    public string FilesSummary { get; }
    public string AddedText { get; }
    public string DeletedText { get; }

    private static string Signer(CommitDetails d) => d.Signer is { } s ? $" · {s}" : "";

    private static string FormatDate(DateTimeOffset when) =>
        when.ToLocalTime().ToString("ddd d MMM yyyy, HH:mm", CultureInfo.CurrentCulture);

    private static string Initials(string name)
    {
        var parts = name.Split([' ', '.', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => (parts[0][..1] + parts[^1][..1]).ToUpperInvariant(),
        };
    }
}

public sealed class ParentViewModel(string hash)
{
    public string Hash { get; } = hash;
    public string ShortHash { get; } = hash[..Math.Min(7, hash.Length)];
}

public sealed class ChangedFileViewModel
{
    public ChangedFileViewModel(ChangedFile f)
    {
        Model = f;
        FileName = f.FileName;
        Directory = f.OldPath is { } old && old != f.Path ? $"{f.Directory}  ← {old}" : f.Directory;
        (Letter, LetterColor) = f.Kind switch
        {
            ChangeKind.Added => ("A", Palette.GreenText),
            ChangeKind.Deleted => ("D", Palette.RedText),
            ChangeKind.Renamed => ("R", Palette.Info),
            ChangeKind.Copied => ("C", Palette.Info),
            ChangeKind.TypeChanged => ("T", Palette.OrangeText),
            _ => ("M", Palette.TextSecondary),
        };
        AddedText = f.IsBinary ? "bin" : f.Added > 0 ? $"+{f.Added}" : "";
        DeletedText = f.IsBinary ? "" : f.Deleted > 0 ? $"−{f.Deleted}" : "";
    }

    public ChangedFile Model { get; }
    public string FileName { get; }
    public string Directory { get; }
    public string Letter { get; }
    public Color LetterColor { get; }
    public string AddedText { get; }
    public string DeletedText { get; }
}
