using System.Runtime.InteropServices;
using CentralGit.Core.Git;

namespace CentralGit.App.Services;

/// <summary>
/// Lists the credentials Git Credential Manager keeps in Windows Credential Manager
/// (targets named "git:https://…"). Only target and user name are read — never the secret.
/// Removing goes through <c>git credential reject</c>, not through this class.
/// </summary>
public static partial class CredentialVault
{
    public static IReadOnlyList<StoredCredential> ListGitCredentials()
    {
        if (!CredEnumerate("git:*", 0, out var count, out var list))
            return [];

        try
        {
            var result = new List<StoredCredential>(count);
            for (var i = 0; i < count; i++)
            {
                var native = Marshal.PtrToStructure<Credential>(Marshal.ReadIntPtr(list, i * IntPtr.Size));
                var target = Marshal.PtrToStringUni(native.TargetName);
                var user = Marshal.PtrToStringUni(native.UserName);
                if (target is not null && StoredCredential.FromTarget(target, user) is { } c)
                    result.Add(c);
            }
            return result
                .DistinctBy(c => (c.Url, c.UserName))
                .OrderBy(c => c.Host, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        finally
        {
            CredFree(list);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [LibraryImport("advapi32.dll", EntryPoint = "CredEnumerateW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredEnumerate(string filter, uint flags, out int count, out IntPtr credentials);

    [LibraryImport("advapi32.dll")]
    private static partial void CredFree(IntPtr buffer);
}
