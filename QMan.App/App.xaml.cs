using System.Windows;

namespace QMan.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        NativeVecBootstrap.EnsureBundledNativeExtracted();
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppContextRoot.Shutdown();
        base.OnExit(e);
    }
}

