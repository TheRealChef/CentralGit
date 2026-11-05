using CentralGit.App.Theming;
using CentralGit.App.ViewModels;
using CentralGit.Application;
using CentralGit.Application.Ports;
using CentralGit.Infrastructure;
using CentralGit.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace CentralGit.App;

/// <summary>What CentralGit was started with: <c>CentralGit.exe [path-to-repository]</c>.</summary>
public sealed record StartupOptions(string? RequestedPath);

/// <summary>
/// The composition root: the only place that knows Infrastructure. Everything else asks for
/// Application services and ports through constructors.
/// </summary>
public partial class App : Microsoft.UI.Xaml.Application
{
    private readonly ServiceProvider _services;
    private Window? _window;

    public App()
    {
        UnhandledException += (_, e) => CrashLog.Write(e.Exception);
        _services = ConfigureServices(new StartupOptions(Environment.GetCommandLineArgs().Skip(1).FirstOrDefault()));
        InitializeComponent();
        ThemeService.Use(_services.GetRequiredService<ISettingsStore>());
        RequestedTheme = ThemeService.StartupApplicationTheme; // only settable in the constructor
    }

    private static ServiceProvider ConfigureServices(StartupOptions startup)
    {
        var services = new ServiceCollection()
            .AddApplication()
            .AddInfrastructure()
            .AddSingleton(startup)
            .AddSingleton<ShellViewModel>()
            .AddSingleton<SwitcherViewModel>()
            .AddSingleton<SettingsViewModel>()
            .AddSingleton<BranchPickerViewModel>()
            .AddSingleton<MainWindow>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            ThemeService.Initialize(Resources);
            _window = _services.GetRequiredService<MainWindow>(); // view models need the UI thread's dispatcher
        }
        catch (Exception e)
        {
            CrashLog.Write(e);
            throw;
        }
        _window.Activate();
    }
}

/// <summary>%APPDATA%\CentralGit\crash.log: the last unhandled exception, for "it closed on startup" reports.</summary>
public static class CrashLog
{
    public static void Write(Exception e)
    {
        try
        {
            File.WriteAllText(AppPaths.File("crash.log"), $"{DateTimeOffset.Now:O}\n{e}\n");
        }
        catch { }
    }
}
