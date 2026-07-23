using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace NativeScreenDimmer_WinUI3;

public sealed partial class MainWindow : Window
{
    private readonly IntPtr _windowHandle;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");
        _windowHandle = WindowNative.GetWindowHandle(this);

        // Navigate the root frame to the main page on startup.
        RootFrame.Navigate(typeof(MainPage));
    }

    public void BringToFront()
    {
        Activate();
    }

    public void ShowFromTray()
    {
        IntPtr windowHandle = WindowNative.GetWindowHandle(this);
        ShowWindow(windowHandle, SwRestore);
        Activate();
        SetForegroundWindow(windowHandle);
    }

    public void HideToTray()
    {
        ShowWindow(WindowNative.GetWindowHandle(this), SwHide);
    }

    public bool IsVisibleOnScreen()
    {
        return IsWindowVisible(_windowHandle);
    }

    public MainPage? GetMainPage()
    {
        return RootFrame.Content as MainPage;
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    private const int SwHide = 0;
    private const int SwRestore = 9;
}
