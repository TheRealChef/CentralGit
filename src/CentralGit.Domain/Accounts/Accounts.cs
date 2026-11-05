namespace CentralGit.Domain.Accounts;

/// <summary>A credential Git Credential Manager has stored, e.g. for "https://dev.azure.com/org". Never the secret.</summary>
public sealed record StoredCredential(string Protocol, string Host, string? Path, string UserName)
{
    public string Url => Path is null ? $"{Protocol}://{Host}" : $"{Protocol}://{Host}/{Path}";
}

public sealed record GitTools(string GitVersion, string? CredentialManagerVersion);
