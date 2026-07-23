using System.Runtime.InteropServices;
using NativeScreenDimmer_WinUI3.Models;

namespace NativeScreenDimmer_WinUI3.Services;

internal sealed class DimmerService : IDisposable
{
    private readonly MonitorTopologyService _monitorTopologyService = new();
    private readonly Dictionary<string, NativeOverlayWindow> _overlaysByDeviceName = new(StringComparer.OrdinalIgnoreCase);

    public void Apply(IReadOnlyList<MonitorDimSetting> settings)
    {
        using IDisposable operation = AppLogger.BeginOperation(nameof(Apply));
        IReadOnlyDictionary<string, MonitorDescriptor> monitorsByDevice = _monitorTopologyService.GetMonitorMap();
        Dictionary<string, MonitorDimSetting> settingsByDevice = settings.ToDictionary(
            setting => setting.DeviceName,
            StringComparer.OrdinalIgnoreCase);

        foreach (MonitorDimSetting setting in settings)
        {
            if (!monitorsByDevice.TryGetValue(setting.DeviceName, out MonitorDescriptor? monitor))
            {
                continue;
            }

            NativeOverlayWindow overlayWindow = GetOrCreateOverlay(setting.DeviceName);
            overlayWindow.Apply(monitor, setting);
        }

        HideMissingOrDisabledOverlays(settingsByDevice, monitorsByDevice);
    }

    public void Dispose()
    {
        foreach (NativeOverlayWindow overlayWindow in _overlaysByDeviceName.Values)
        {
            overlayWindow.Dispose();
        }

        _overlaysByDeviceName.Clear();
    }

    private NativeOverlayWindow GetOrCreateOverlay(string deviceName)
    {
        if (_overlaysByDeviceName.TryGetValue(deviceName, out NativeOverlayWindow? existingOverlay))
        {
            return existingOverlay;
        }

        NativeOverlayWindow createdOverlay = new();
        _overlaysByDeviceName[deviceName] = createdOverlay;
        AppLogger.LogInfo($"Created native overlay for '{deviceName}'.");
        return createdOverlay;
    }

    private void HideMissingOrDisabledOverlays(
        IReadOnlyDictionary<string, MonitorDimSetting> settingsByDevice,
        IReadOnlyDictionary<string, MonitorDescriptor> monitorsByDevice)
    {
        foreach ((string deviceName, NativeOverlayWindow overlayWindow) in _overlaysByDeviceName)
        {
            if (!monitorsByDevice.ContainsKey(deviceName)
                || !settingsByDevice.TryGetValue(deviceName, out MonitorDimSetting? setting)
                || !setting.IsEnabled
                || setting.DimPercent <= 0)
            {
                overlayWindow.Hide();
            }
        }
    }
}

internal sealed class NativeOverlayWindow : IDisposable
{
    private static readonly IntPtr TopmostWindowHandle = new(-1);
    private static readonly Lock OverlayGate = new();
    private static readonly Dictionary<IntPtr, NativeOverlayWindow> OverlayByHandle = [];
    private static readonly WindowProcedureDelegate WindowProcedure = HandleWindowMessage;
    private static readonly WinEventDelegate ShellEventCallback = HandleShellEvent;
    private static bool _windowClassRegistered;
    private static ushort _windowClassAtom;
    private static IntPtr _foregroundEventHook;
    private static IntPtr _objectShowEventHook;
    private static IntPtr _objectReorderEventHook;

    private IntPtr _windowHandle;
    private uint _backgroundColorRef = 0x000000;
    private byte _alphaByte;
    private bool _isVisible;
    private bool _disposed;

    public void Apply(MonitorDescriptor monitor, MonitorDimSetting setting)
    {
        EnsureCreated();

        _backgroundColorRef = ToColorRef(setting.ColorHex);
        _alphaByte = (byte)Math.Clamp((int)Math.Round(setting.DimPercent * 255.0 / 100.0), 0, 242);

        if (!setting.IsEnabled || setting.DimPercent <= 0)
        {
            Hide();
            return;
        }

        _isVisible = true;
        SetWindowPos(
            _windowHandle,
            TopmostWindowHandle,
            monitor.Left,
            monitor.Top,
            monitor.Width,
            monitor.Height,
            SwpNoActivate | SwpShowWindow);

        SetLayeredWindowAttributes(_windowHandle, 0, _alphaByte, LwaAlpha);
        InvalidateRect(_windowHandle, IntPtr.Zero, false);
        ShowWindow(_windowHandle, SwShownoactivate);
    }

    public void Hide()
    {
        if (_windowHandle == IntPtr.Zero)
        {
            return;
        }

        _isVisible = false;
        ShowWindow(_windowHandle, SwHide);
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
            lock (OverlayGate)
            {
                OverlayByHandle.Remove(_windowHandle);
                StopForegroundWatcherIfUnused();
            }

            DestroyWindow(_windowHandle);
            _windowHandle = IntPtr.Zero;
        }
    }

    private void EnsureCreated()
    {
        if (_windowHandle != IntPtr.Zero)
        {
            return;
        }

        RegisterWindowClass();
        EnsureForegroundWatcher();
        _windowHandle = CreateWindowEx(
            WsExTopmost | WsExLayered | WsExTransparent | WsExToolWindow | WsExNoActivate,
            WindowClassName,
            string.Empty,
            WsPopup,
            0,
            0,
            1,
            1,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        if (_windowHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"CreateWindowEx failed with Win32 error {Marshal.GetLastWin32Error()}.");
        }

        lock (OverlayGate)
        {
            OverlayByHandle[_windowHandle] = this;
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
            Instance = IntPtr.Zero,
            Icon = IntPtr.Zero,
            Cursor = IntPtr.Zero,
            BackgroundBrush = IntPtr.Zero,
            MenuName = string.Empty,
            ClassName = WindowClassName
        };

        _windowClassAtom = RegisterClass(ref windowClass);
        if (_windowClassAtom == 0)
        {
            throw new InvalidOperationException($"RegisterClass failed with Win32 error {Marshal.GetLastWin32Error()}.");
        }

        _windowClassRegistered = true;
    }

    private static IntPtr HandleWindowMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        NativeOverlayWindow? overlay;
        lock (OverlayGate)
        {
            if (!OverlayByHandle.TryGetValue(hwnd, out overlay))
            {
                return DefWindowProc(hwnd, message, wParam, lParam);
            }
        }

        if (message == WmPaint)
        {
            if (overlay is null)
            {
                return DefWindowProc(hwnd, message, wParam, lParam);
            }

            PaintStruct paintStruct = new();
            IntPtr hdc = BeginPaint(hwnd, ref paintStruct);
            IntPtr brush = CreateSolidBrush(overlay._backgroundColorRef);
            FillRect(hdc, ref paintStruct.PaintRectangle, brush);
            DeleteObject(brush);
            EndPaint(hwnd, ref paintStruct);
            return IntPtr.Zero;
        }

        if (message == WmErasebkgnd)
        {
            return new IntPtr(1);
        }

        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private static void EnsureForegroundWatcher()
    {
        if (_foregroundEventHook != IntPtr.Zero
            && _objectShowEventHook != IntPtr.Zero
            && _objectReorderEventHook != IntPtr.Zero)
        {
            return;
        }

        _foregroundEventHook = SetWinEventHook(
            EventSystemForeground,
            EventSystemForeground,
            IntPtr.Zero,
            ShellEventCallback,
            0,
            0,
            WineventOutofcontext | WineventSkipownprocess);

        _objectShowEventHook = SetWinEventHook(
            EventObjectShow,
            EventObjectShow,
            IntPtr.Zero,
            ShellEventCallback,
            0,
            0,
            WineventOutofcontext | WineventSkipownprocess);

        _objectReorderEventHook = SetWinEventHook(
            EventObjectReorder,
            EventObjectReorder,
            IntPtr.Zero,
            ShellEventCallback,
            0,
            0,
            WineventOutofcontext | WineventSkipownprocess);
    }

    private static void StopForegroundWatcherIfUnused()
    {
        if (OverlayByHandle.Count != 0)
        {
            return;
        }

        if (_foregroundEventHook != IntPtr.Zero)
        {
            UnhookWinEvent(_foregroundEventHook);
            _foregroundEventHook = IntPtr.Zero;
        }

        if (_objectShowEventHook != IntPtr.Zero)
        {
            UnhookWinEvent(_objectShowEventHook);
            _objectShowEventHook = IntPtr.Zero;
        }

        if (_objectReorderEventHook != IntPtr.Zero)
        {
            UnhookWinEvent(_objectReorderEventHook);
            _objectReorderEventHook = IntPtr.Zero;
        }
    }

    private static void HandleShellEvent(
        IntPtr hookHandle,
        uint eventType,
        IntPtr windowHandle,
        int objectId,
        int childId,
        uint eventThreadId,
        uint eventTime)
    {
        if (objectId != ObjidWindow || childId != 0)
        {
            return;
        }

        List<NativeOverlayWindow> overlays;
        lock (OverlayGate)
        {
            overlays = OverlayByHandle.Values.ToList();
        }

        foreach (NativeOverlayWindow overlay in overlays)
        {
            overlay.ReassertTopmostBurstIfVisible();
        }
    }

    private void ReassertTopmostBurstIfVisible()
    {
        if (_windowHandle == IntPtr.Zero || !_isVisible || _disposed)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            ReassertTopmostCore();
            await Task.Delay(24).ConfigureAwait(false);
            ReassertTopmostCore();
            await Task.Delay(96).ConfigureAwait(false);
            ReassertTopmostCore();
        });
    }

    private void ReassertTopmostCore()
    {
        if (_windowHandle == IntPtr.Zero || !_isVisible || _disposed)
        {
            return;
        }

        _ = SetWindowPos(
            _windowHandle,
            TopmostWindowHandle,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);
    }

    private static uint ToColorRef(string colorHex)
    {
        if (string.IsNullOrWhiteSpace(colorHex))
        {
            return 0x000000;
        }

        string trimmed = colorHex.Trim();
        System.Drawing.Color color;
        try
        {
            if (trimmed.StartsWith('#') && trimmed.Length == 7 && trimmed[1..].All(Uri.IsHexDigit))
            {
                color = System.Drawing.ColorTranslator.FromHtml(trimmed);
            }
            else
            {
                color = System.Drawing.Color.FromName(trimmed);
                if (!color.IsKnownColor)
                {
                    color = System.Drawing.Color.Black;
                }
            }
        }
        catch (ArgumentException)
        {
            color = System.Drawing.Color.Black;
        }

        return (uint)(color.R | (color.G << 8) | (color.B << 16));
    }

    private const string WindowClassName = "NativeScreenDimmerOverlayWindowClass";
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsExTopmost = 0x00000008;
    private const int WsExLayered = 0x00080000;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const uint LwaAlpha = 0x00000002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const uint SwpShowWindow = 0x0040;
    private const int SwHide = 0;
    private const int SwShownoactivate = 4;
    private const uint WmPaint = 0x000F;
    private const uint WmErasebkgnd = 0x0014;
    private const uint EventSystemForeground = 0x0003;
    private const uint EventObjectShow = 0x8002;
    private const uint EventObjectReorder = 0x8004;
    private const uint WineventOutofcontext = 0x0000;
    private const uint WineventSkipownprocess = 0x0002;
    private const int ObjidWindow = 0;

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

    [StructLayout(LayoutKind.Sequential)]
    private struct PaintStruct
    {
        public IntPtr DeviceContext;
        [MarshalAs(UnmanagedType.Bool)] public bool Erase;
        public Rect PaintRectangle;
        [MarshalAs(UnmanagedType.Bool)] public bool Restore;
        [MarshalAs(UnmanagedType.Bool)] public bool IncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate IntPtr WindowProcedureDelegate(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    private delegate void WinEventDelegate(
        IntPtr hookHandle,
        uint eventType,
        IntPtr windowHandle,
        int objectId,
        int childId,
        uint eventThreadId,
        uint eventTime);

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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(
        IntPtr windowHandle,
        uint colorKey,
        byte alpha,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool InvalidateRect(IntPtr windowHandle, IntPtr rect, bool erase);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);

    [DllImport("user32.dll")]
    private static extern IntPtr BeginPaint(IntPtr windowHandle, ref PaintStruct paintStruct);

    [DllImport("user32.dll")]
    private static extern bool EndPaint(IntPtr windowHandle, ref PaintStruct paintStruct);

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr deviceContext, ref Rect rect, IntPtr brush);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(uint colorRef);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr objectHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr moduleHandle,
        WinEventDelegate callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hookHandle);
}
