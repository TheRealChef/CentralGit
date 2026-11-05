using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Velopack;

namespace CentralGit.App;

/// <summary>
/// Entry point (replaces the XAML-generated Main). Velopack runs first: during install,
/// update and uninstall it calls the exe with hook arguments, handles them and exits
/// before any window exists.
/// </summary>
public static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Microsoft.UI.Xaml.Application.Start(static _ =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread())
            );
            new App();
        });
    }
}
