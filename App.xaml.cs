using Microsoft.UI.Xaml;
using NativeScreenDimmer_WinUI3.Services;

namespace NativeScreenDimmer_WinUI3;

public partial class App : Application
{
    private MainWindow? _window;
    private TrayService? _trayService;
    private bool _allowMainWindowClose;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            AppLogger.LogError($"UnhandledException: {e.Exception?.GetType().Name}: {e.Exception?.Message}\n{e.Exception?.StackTrace}");
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            AppLogger.LogError($"AppDomain.UnhandledException: {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) =>
            AppLogger.LogError($"UnobservedTaskException: {e.Exception?.Message}");
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        AppLogger.LogInfo("Main window created.");
        _window.AppWindow.Closing += MainWindow_Closing;
        _trayService = new TrayService(
            _window.IsVisibleOnScreen,
            () => { _window.DispatcherQueue.TryEnqueue(() => _window.ShowFromTray()); },
            () => { _window.DispatcherQueue.TryEnqueue(() => _window.HideToTray()); },
            () => { _window.DispatcherQueue.TryEnqueue(() => ExecuteOnMainPage(mainPage => mainPage.ApplyBalancedPreset())); },
            () => { _window.DispatcherQueue.TryEnqueue(() => ExecuteOnMainPage(mainPage => mainPage.ApplyEveningPreset())); },
            () => { _window.DispatcherQueue.TryEnqueue(() => ExecuteOnMainPage(mainPage => mainPage.ApplyPerformancePreset())); },
            () => { _window.DispatcherQueue.TryEnqueue(() => ExecuteOnMainPage(mainPage => mainPage.SetAutomationEnabledFromTray(true))); },
            () => { _window.DispatcherQueue.TryEnqueue(() => ExecuteOnMainPage(mainPage => mainPage.SetAutomationEnabledFromTray(false))); },
            () => { _window.DispatcherQueue.TryEnqueue(RequestExit); });
        _window.Activate();
    }

    public MainWindow? MainWindowInstance => _window;

    public void RequestExit()
    {
        if (_window is null)
        {
            Exit();
            return;
        }

        _allowMainWindowClose = true;
        _trayService?.Dispose();
        _trayService = null;
        _window.Close();
    }

    private void MainWindow_Closing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (_allowMainWindowClose)
        {
            return;
        }

        args.Cancel = true;
        _trayService?.HideWindow();
    }

    private void ExecuteOnMainPage(Action<MainPage> action)
    {
        MainPage? mainPage = _window?.GetMainPage();
        if (mainPage is null)
        {
            return;
        }

        action(mainPage);
    }
}
