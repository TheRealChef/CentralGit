using Microsoft.UI.Xaml;

namespace CentralGit.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // CentralGit.exe [path-to-repository]
        var path = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault();
        _window = new MainWindow(path);
        _window.Activate();
    }
}
