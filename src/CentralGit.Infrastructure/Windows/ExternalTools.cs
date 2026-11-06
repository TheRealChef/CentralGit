using System.Diagnostics;
using CentralGit.Application.Ports;

namespace CentralGit.Infrastructure.Windows;

/// <summary>
/// Explorer (with the file selected), Windows Terminal (falling back to PowerShell) and VS Code
/// (falling back to the file's default app). Nothing waits for the started program.
/// </summary>
public sealed class ExternalTools : IExternalTools
{
    public string? Open(ExternalTool tool, string folder, string? file = null)
    {
        var path = file is null ? null : Path.GetFullPath(Path.Combine(folder, file));
        try
        {
            switch (tool)
            {
                case ExternalTool.Explorer:
                    Start("explorer.exe", path is not null && File.Exists(path) ? [$"/select,{path}"] : [folder]);
                    return null;
                case ExternalTool.Terminal:
                    if (FindOnPath("wt.exe") is { } wt)
                        Start(wt, ["-d", folder]);
                    else
                        Start("powershell.exe", ["-NoExit"], folder);
                    return null;
                case ExternalTool.Editor:
                    if (FindVsCode() is { } code)
                    {
                        // code.cmd is a batch file: it needs cmd, and a hidden window for cmd itself.
                        var args = path is null ? [folder] : new[] { folder, "--goto", path };
                        if (code.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
                            Start("cmd.exe", ["/c", code, .. args], folder, hidden: true);
                        else
                            Start(code, args, folder);
                        return null;
                    }
                    if (path is not null && File.Exists(path))
                    {
                        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
                        return null;
                    }
                    return "No code editor found. Install VS Code (with “Add to PATH”) to open the repository from here.";
                default:
                    return null;
            }
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return e.Message;
        }
    }

    public string? OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            return $"Not a web address: {url}";
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
            return null;
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            return e.Message;
        }
    }

    private static void Start(
        string exe,
        IEnumerable<string> args,
        string? workingDirectory = null,
        bool hidden = false
    )
    {
        var info = new ProcessStartInfo(exe)
        {
            UseShellExecute = !hidden,
            CreateNoWindow = hidden,
            WorkingDirectory = workingDirectory ?? "",
        };
        foreach (var arg in args)
            info.ArgumentList.Add(arg);
        Process.Start(info)?.Dispose();
    }

    private static string? FindVsCode()
    {
        var installed = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "Microsoft VS Code",
            "Code.exe"
        );
        return File.Exists(installed) ? installed : FindOnPath("code.cmd");
    }

    private static string? FindOnPath(string name)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (dir.Length == 0)
                continue;
            try
            {
                var candidate = Path.Combine(dir.Trim('"'), name);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch (ArgumentException) { }
        }
        // Windows Terminal lives behind an app execution alias in WindowsApps, which is usually on PATH;
        // check it directly in case it isn't.
        var alias = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "WindowsApps",
            name
        );
        return File.Exists(alias) ? alias : null;
    }
}
