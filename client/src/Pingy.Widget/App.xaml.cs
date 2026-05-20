using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
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

        // Wire crash diagnostics BEFORE anything else can throw. All three sources
        // converge on CrashLogger so a "suddenly closed" report leaves a forensic trail.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        CrashLogger.Log("startup", null, $"Pingy.Widget starting — log path {CrashLogger.LogPath}");

        var loader = new JsonTargetLoader();
        var pinger = new Pinger();
        var portProbe = new TcpPortProbe();
        _serviceCheck = new HttpServiceCheck();
        ViewModel = new MainViewModel(pinger, portProbe, _serviceCheck, loader);
    }

    private HttpServiceCheck? _serviceCheck;

    protected override void OnExit(ExitEventArgs e)
    {
        ViewModel?.Stop();
        _serviceCheck?.Dispose();
        CrashLogger.Log("exit", null, $"ExitCode={e.ApplicationExitCode}");
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        CrashLogger.Log("dispatcher", e.Exception);
        // Keep the app alive — a single bad UI callback shouldn't tear down the widget.
        // If the exception is truly fatal it'll resurface on the next op; meanwhile the user
        // gets a chance to read the log instead of finding an empty taskbar.
        e.Handled = true;
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        CrashLogger.Log("appdomain", e.ExceptionObject as Exception, $"IsTerminating={e.IsTerminating}");
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        CrashLogger.Log("task", e.Exception);
        e.SetObserved();
    }
}
