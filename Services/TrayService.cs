using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace NativeScreenDimmer_WinUI3.Services;

internal sealed class TrayService : IDisposable
{
    private const string WindowClassName = "NativeScreenDimmerTrayWindowClass";
    private const int HwndMessage = -3;
    private const uint TrayCallbackMessage = 0x8001;
    private const uint WmTrayIcon = TrayCallbackMessage;
    private const uint WmClose = 0x0010;
    private const uint WmDestroy = 0x0002;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmLButtonDblClk = 0x0203;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmContextMenu = 0x007B;
    private const uint WmNull = 0x0000;
    private const uint NimAdd = 0x00000000;
    private const uint NimDelete = 0x00000002;
    private const uint NimSetVersion = 0x00000004;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NotifyIconVersion4 = 4;
    private const uint MfString = 0x0000;
    private const uint MfSeparator = 0x0800;
    private const uint TpmReturnCmd = 0x0100;
    private const uint TpmRightButton = 0x0002;
    private const uint IdShowWindow = 1001;
    private const uint IdHideWindow = 1002;
    private const uint IdPresetBalanced = 1101;
    private const uint IdPresetEvening = 1102;
    private const uint IdPresetPerformance = 1103;
    private const uint IdAutomationEnable = 1201;
    private const uint IdAutomationDisable = 1202;
    private const uint IdExitApp = 1900;
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int HwndMessageParent = -3;

    private static readonly IntPtr MessageOnlyParent = new(HwndMessageParent);
    private static readonly Lock Gate = new();
    private static readonly Dictionary<IntPtr, TrayService> ServiceByWindowHandle = [];
    private static readonly WindowProcedureDelegate WindowProcedure = HandleWindowMessage;
    private static bool _windowClassRegistered;

    private readonly Action _showWindow;
    private readonly Action _hideWindow;
    private readonly Action _applyBalancedPreset;
    private readonly Action _applyEveningPreset;
    private readonly Action _applyPerformancePreset;
    private readonly Action _enableAutomation;
    private readonly Action _disableAutomation;
    private readonly Action _requestExit;
    private readonly Func<bool> _isWindowVisible;
    private readonly Thread _trayThread;
    private readonly TaskCompletionSource _startupCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ManualResetEventSlim _shutdownSignal = new(false);

    private IntPtr _windowHandle;
    private IntPtr _iconHandle;
    private bool _ownsIconHandle;
    private uint _trayThreadId;
    private bool _disposed;

    public TrayService(
        Func<bool> isWindowVisible,
        Action showWindow,
        Action hideWindow,
        Action applyBalancedPreset,
        Action applyEveningPreset,
        Action applyPerformancePreset,
        Action enableAutomation,
        Action disableAutomation,
        Action requestExit)
    {
        _isWindowVisible = isWindowVisible;
        _showWindow = showWindow;
        _hideWindow = hideWindow;
        _applyBalancedPreset = applyBalancedPreset;
        _applyEveningPreset = applyEveningPreset;
        _applyPerformancePreset = applyPerformancePreset;
        _enableAutomation = enableAutomation;
        _disableAutomation = disableAutomation;
        _requestExit = requestExit;

        using IDisposable operation = AppLogger.BeginOperation(nameof(TrayService));
        _trayThread = new Thread(TrayThreadProc)
        {
            IsBackground = true,
            Name = "NoirLumen Tray Thread"
        };
        _trayThread.SetApartmentState(ApartmentState.STA);
        _trayThread.Start();

        _startupCompletionSource.Task.GetAwaiter().GetResult();
    }

    public void ShowWindow()
    {
        _showWindow();
    }

    public void HideWindow()
    {
        _hideWindow();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_windowHandle != IntPtr.Zero)
        {
            PostMessage(_windowHandle, WmClose, IntPtr.Zero, IntPtr.Zero);
        }

        if (!_shutdownSignal.Wait(TimeSpan.FromSeconds(5)))
        {
            AppLogger.LogWarning("Tray service shutdown timed out waiting for tray thread signal.");
        }

        if (_trayThread.IsAlive)
        {
            if (!_trayThread.Join(TimeSpan.FromSeconds(2)))
            {
                AppLogger.LogWarning("Tray service shutdown timed out waiting for tray thread join.");
            }
        }
    }

    private void TrayThreadProc()
    {
        try
        {
            _trayThreadId = GetCurrentThreadId();
            RegisterWindowClass();
            EnsureWindowCreated();
            EnsureIconAdded();
            AppLogger.LogInfo("Tray host thread initialized.");
            _startupCompletionSource.TrySetResult();
        }
        catch (Exception exception)
        {
            AppLogger.LogException("Tray host initialization failed", exception);
            _startupCompletionSource.TrySetException(ExceptionDispatchInfo.Capture(exception).SourceException);
            _shutdownSignal.Set();
            return;
        }

        try
        {
            RunMessageLoop();
        }
        finally
        {
            Cleanup();
            _shutdownSignal.Set();
        }
    }

    private void RunMessageLoop()
    {
        while (GetMessage(out Message message, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }
    }

    private void Cleanup()
    {
        if (_windowHandle != IntPtr.Zero)
        {
            lock (Gate)
            {
                ServiceByWindowHandle.Remove(_windowHandle);
            }

            RemoveIcon();
            DestroyWindow(_windowHandle);
            _windowHandle = IntPtr.Zero;
        }

        if (_iconHandle != IntPtr.Zero && _ownsIconHandle)
        {
            DestroyIcon(_iconHandle);
            _iconHandle = IntPtr.Zero;
        }
    }

    private void EnsureWindowCreated()
    {
        _windowHandle = CreateWindowEx(
            WsExToolWindow | WsExNoActivate,
            WindowClassName,
            string.Empty,
            WsPopup,
            0,
            0,
            1,
            1,
            MessageOnlyParent,
            IntPtr.Zero,
            GetModuleHandle(null),
            IntPtr.Zero);

        if (_windowHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"CreateWindowEx failed with Win32 error {Marshal.GetLastWin32Error()}.");
        }

        lock (Gate)
        {
            ServiceByWindowHandle[_windowHandle] = this;
        }
    }

    private void EnsureIconAdded()
    {
        _iconHandle = LoadTrayIcon(out _ownsIconHandle);

        NotifyIconData notifyIconData = new()
        {
            CbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
            HWnd = _windowHandle,
            UId = 1,
            UFlags = NifMessage | NifIcon | NifTip,
            UCallbackMessage = WmTrayIcon,
            HIcon = _iconHandle,
            SzTip = "NoirLumen Control Center",
            DwState = 0,
            DwStateMask = 0,
            SzInfo = string.Empty,
            UTimeoutOrVersion = NotifyIconVersion4,
            SzInfoTitle = string.Empty,
            DwInfoFlags = 0,
            GuidItem = Guid.Empty,
            HBalloonIcon = IntPtr.Zero
        };

        if (!Shell_NotifyIcon(NimAdd, ref notifyIconData))
        {
            throw new InvalidOperationException($"Shell_NotifyIcon(NIM_ADD) failed with Win32 error {Marshal.GetLastWin32Error()}.");
        }

        if (!Shell_NotifyIcon(NimSetVersion, ref notifyIconData))
        {
            AppLogger.LogWarning($"Tray icon version setup failed with Win32 error {Marshal.GetLastWin32Error()}.");
        }

        AppLogger.LogInfo("Tray icon added.");
    }

    private void RemoveIcon()
    {
        if (_windowHandle == IntPtr.Zero)
        {
            return;
        }

        NotifyIconData notifyIconData = new()
        {
            CbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
            HWnd = _windowHandle,
            UId = 1
        };

        Shell_NotifyIcon(NimDelete, ref notifyIconData);
    }

    private void HandleTrayAction(uint action)
    {
        switch (action)
        {
            case WmLButtonUp:
            case WmLButtonDblClk:
                AppLogger.LogInfo("Tray icon activated.");
                _showWindow();
                break;
            case WmRButtonUp:
            case WmContextMenu:
                ShowContextMenu();
                break;
        }
    }

    private void ShowContextMenu()
    {
        if (_windowHandle == IntPtr.Zero)
        {
            return;
        }

        IntPtr menuHandle = CreatePopupMenu();
        if (menuHandle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            bool isWindowVisible = _isWindowVisible();
            uint hideShowCommand = isWindowVisible ? IdHideWindow : IdShowWindow;
            string hideShowText = isWindowVisible ? "Hide window" : "Show window";

            AppendMenu(menuHandle, MfString, hideShowCommand, hideShowText);
            AppendMenu(menuHandle, MfSeparator, 0, string.Empty);
            AppendMenu(menuHandle, MfString, IdPresetBalanced, "Preset: Balanced");
            AppendMenu(menuHandle, MfString, IdPresetEvening, "Preset: Evening");
            AppendMenu(menuHandle, MfString, IdPresetPerformance, "Preset: Performance");
            AppendMenu(menuHandle, MfSeparator, 0, string.Empty);
            AppendMenu(menuHandle, MfString, IdAutomationEnable, "Automation: On");
            AppendMenu(menuHandle, MfString, IdAutomationDisable, "Automation: Off");
            AppendMenu(menuHandle, MfSeparator, 0, string.Empty);
            AppendMenu(menuHandle, MfString, IdExitApp, "Exit");

            GetCursorPos(out Point cursorPoint);
            SetForegroundWindow(_windowHandle);
            uint selectedCommand = TrackPopupMenuEx(
                menuHandle,
                TpmReturnCmd | TpmRightButton,
                cursorPoint.X,
                cursorPoint.Y,
                _windowHandle,
                IntPtr.Zero);

            if (selectedCommand == IdShowWindow)
            {
                AppLogger.LogInfo("Tray menu selected: show window.");
                _showWindow();
            }
            else if (selectedCommand == IdHideWindow)
            {
                AppLogger.LogInfo("Tray menu selected: hide window.");
                _hideWindow();
            }
            else if (selectedCommand == IdExitApp)
            {
                AppLogger.LogInfo("Tray menu selected: exit.");
                _requestExit();
            }
            else if (selectedCommand == IdPresetBalanced)
            {
                AppLogger.LogInfo("Tray menu selected: preset balanced.");
                _applyBalancedPreset();
            }
            else if (selectedCommand == IdPresetEvening)
            {
                AppLogger.LogInfo("Tray menu selected: preset evening.");
                _applyEveningPreset();
            }
            else if (selectedCommand == IdPresetPerformance)
            {
                AppLogger.LogInfo("Tray menu selected: preset performance.");
                _applyPerformancePreset();
            }
            else if (selectedCommand == IdAutomationEnable)
            {
                AppLogger.LogInfo("Tray menu selected: automation on.");
                _enableAutomation();
            }
            else if (selectedCommand == IdAutomationDisable)
            {
                AppLogger.LogInfo("Tray menu selected: automation off.");
                _disableAutomation();
            }

            PostMessage(_windowHandle, WmNull, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            DestroyMenu(menuHandle);
        }
    }

    private static void RegisterWindowClass()
    {
        if (_windowClassRegistered)
        {
            return;
        }

        WindowClass windowClass = new()
        {
            Style = 0,
            WindowProcedure = WindowProcedure,
            ClassExtraBytes = 0,
            WindowExtraBytes = 0,
            Instance = GetModuleHandle(null),
            Icon = IntPtr.Zero,
            Cursor = IntPtr.Zero,
            BackgroundBrush = IntPtr.Zero,
            MenuName = string.Empty,
            ClassName = WindowClassName
        };

        ushort atom = RegisterClass(ref windowClass);
        if (atom == 0)
        {
            throw new InvalidOperationException($"RegisterClass failed with Win32 error {Marshal.GetLastWin32Error()}.");
        }

        _windowClassRegistered = true;
    }

    private static IntPtr HandleWindowMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        lock (Gate)
        {
            if (!ServiceByWindowHandle.TryGetValue(hwnd, out TrayService? trayService))
            {
                return DefWindowProc(hwnd, message, wParam, lParam);
            }

            if (message == WmTrayIcon)
            {
                trayService!.HandleTrayAction((uint)lParam.ToInt64());
                return IntPtr.Zero;
            }

            if (message == WmClose)
            {
                DestroyWindow(hwnd);
                return IntPtr.Zero;
            }

            if (message == WmDestroy)
            {
                trayService!.RemoveIcon();
                PostQuitMessage(0);
                return IntPtr.Zero;
            }
        }

        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private static IntPtr LoadTrayIcon(out bool ownsIconHandle)
    {
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(iconPath))
        {
            IntPtr iconHandle = LoadImage(IntPtr.Zero, iconPath, IconImage, 0, 0, LoadFromFile | LoadDefaultSize);
            if (iconHandle != IntPtr.Zero)
            {
                ownsIconHandle = true;
                return iconHandle;
            }
        }

        IntPtr fallbackIconHandle = LoadIcon(IntPtr.Zero, IdiApplication);
        if (fallbackIconHandle != IntPtr.Zero)
        {
            ownsIconHandle = false;
            return fallbackIconHandle;
        }

        ownsIconHandle = false;
        throw new InvalidOperationException($"Unable to load tray icon with Win32 error {Marshal.GetLastWin32Error()}.");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Style;
        public WindowProcedureDelegate WindowProcedure;
        public int ClassExtraBytes;
        public int WindowExtraBytes;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr BackgroundBrush;
        [MarshalAs(UnmanagedType.LPWStr)] public string MenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string ClassName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint CbSize;
        public IntPtr HWnd;
        public uint UId;
        public uint UFlags;
        public uint UCallbackMessage;
        public IntPtr HIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string SzTip;
        public uint DwState;
        public uint DwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string SzInfo;
        public uint UTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string SzInfoTitle;
        public uint DwInfoFlags;
        public Guid GuidItem;
        public IntPtr HBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr HWnd;
        public uint Msg;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Point;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WindowProcedureDelegate(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClass(ref WindowClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int exStyle,
        string className,
        string windowName,
        int style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr iconHandle);

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData notifyIconData);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr menuHandle, uint flags, uint newItemId, string newItemText);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menuHandle);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(
        IntPtr menuHandle,
        uint flags,
        int x,
        int y,
        IntPtr windowHandle,
        IntPtr reserved);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Message message, IntPtr windowHandle, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage([In] ref Message message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage([In] ref Message message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr instanceHandle, string fileName, uint type, int cx, int cy, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr LoadIcon(IntPtr instanceHandle, IntPtr iconName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private const uint IconImage = 1;
    private const uint LoadFromFile = 0x0010;
    private const uint LoadDefaultSize = 0x0040;
    private static readonly IntPtr IdiApplication = new(32512);
}
