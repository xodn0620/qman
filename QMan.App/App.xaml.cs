using System.Windows;

namespace QMan.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnExit(ExitEventArgs e)
    {
        AppContextRoot.Shutdown();
        base.OnExit(e);
    }
}

