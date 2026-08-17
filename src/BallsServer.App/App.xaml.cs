using System.Windows;
using BallsServer.Presentation;
using BallsServer.Windows;

namespace BallsServer.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var presentation = new HostDashboardPresentation(
            WindowsPreflightFactory.CreateHostService(),
            new SystemFolderValidator(),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        var window = new MainWindow(presentation);
        MainWindow = window;
        window.Show();
    }
}
