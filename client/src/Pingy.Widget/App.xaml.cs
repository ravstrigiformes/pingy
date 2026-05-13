using System.Windows;
using Pingy.Core.Config;
using Pingy.Core.Probing;
using Pingy.Widget.ViewModels;

namespace Pingy.Widget;

public partial class App : Application
{
    public MainViewModel? ViewModel { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var loader = new JsonTargetLoader();
        var pinger = new Pinger();
        ViewModel = new MainViewModel(pinger, loader);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ViewModel?.Stop();
        base.OnExit(e);
    }
}
