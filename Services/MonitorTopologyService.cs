using System.Runtime.InteropServices;
using System.Management;
using System.Text;

namespace NativeScreenDimmer_WinUI3.Services;

internal sealed class MonitorTopologyService : IDisposable
{
    private const uint WmDisplayChange = 0x007E;
    private const uint WmDeviceChange = 0x0219;
    private const int HwndMessage = -3;
    private const string WindowClassName = "NativeScreenDimmerMonitorTopologyWindowClass";

    private static readonly IntPtr MessageOnlyWindowHandle = new(HwndMessage);
    private static readonly Dictionary<IntPtr, MonitorTopologyService> ServiceByWindowHandle = [];
    private static readonly WindowProcedureDelegate WindowProcedure = HandleWindowMessage;
    private static bool _windowClassRegistered;

    private readonly Lock _gate = new();
    private IntPtr _windowHandle;
    private IReadOnlyList<MonitorDescriptor> _cachedMonitors = [];
    private IReadOnlyDictionary<string, MonitorDescriptor> _cachedMonitorsByDeviceName = new Dictionary<string, MonitorDescriptor>(StringComparer.OrdinalIgnoreCase);
    private bool _isDirty = true;
    private bool _disposed;

    public event EventHandler? TopologyChanged;

    public IReadOnlyList<MonitorDescriptor> GetMonitors()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureWatcherWindow();
            if (_isDirty)
            {
                RefreshCache();
            }

            return _cachedMonitors;
        }
    }

    public IReadOnlyDictionary<string, MonitorDescriptor> GetMonitorMap()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureWatcherWindow();
            if (_isDirty)
            {
                RefreshCache();
            }

            return _cachedMonitorsByDeviceName;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_windowHandle != IntPtr.Zero)
            {
                ServiceByWindowHandle.Remove(_windowHandle);
                DestroyWindow(_windowHandle);
                _windowHandle = IntPtr.Zero;
            }
        }
    }

    private void EnsureWatcherWindow()
    {
        if (_windowHandle != IntPtr.Zero)
        {
            return;
        }

        RegisterWindowClass();
        _windowHandle = CreateWindowEx(
            0,
            WindowClassName,
            string.Empty,
            0,
            0,
            0,
            0,
            0,
            MessageOnlyWindowHandle,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        if (_windowHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"CreateWindowEx failed with Win32 error {Marshal.GetLastWin32Error()}.");
        }

        ServiceByWindowHandle[_windowHandle] = this;
    }

    private void RefreshCache()
    {
        IReadOnlyList<MonitorDescriptor> monitors = EnumerateMonitors();
        _cachedMonitors = monitors;
        _cachedMonitorsByDeviceName = monitors.ToDictionary(monitor => monitor.DeviceName, StringComparer.OrdinalIgnoreCase);
        _isDirty = false;
    }

    private void InvalidateCache()
    {
        bool shouldNotify = false;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (!_isDirty)
            {
                _isDirty = true;
                shouldNotify = true;
            }
        }

        if (shouldNotify)
        {
            TopologyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static IReadOnlyList<MonitorDescriptor> EnumerateMonitors()
    {
        IReadOnlyDictionary<string, string> modelNameByDeviceName = GetMonitorModelNameByDeviceName();
        List<MonitorDescriptor> monitors = [];
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitorHandle, _, _, _) =>
        {
            MonitorInfoEx monitorInfo = MonitorInfoEx.Create();
            if (!GetMonitorInfo(monitorHandle, ref monitorInfo))
            {
                return true;
            }

            int width = monitorInfo.MonitorArea.Right - monitorInfo.MonitorArea.Left;
            int height = monitorInfo.MonitorArea.Bottom - monitorInfo.MonitorArea.Top;
            if (width <= 0 || height <= 0)
            {
                return true;
            }

            string deviceName = monitorInfo.DeviceName.TrimEnd('\0');
            monitors.Add(new MonitorDescriptor
            {
                DeviceName = deviceName,
                ModelName = modelNameByDeviceName.TryGetValue(deviceName, out string? modelName)
                    ? modelName
                    : string.Empty,
                IsPrimary = (monitorInfo.Flags & MonitorInfofPrimary) != 0,
                Left = monitorInfo.MonitorArea.Left,
                Top = monitorInfo.MonitorArea.Top,
                Width = width,
                Height = height
            });

            return true;
        }, IntPtr.Zero);

        return monitors
            .OrderByDescending(monitor => monitor.IsPrimary)
            .ThenBy(monitor => monitor.Left)
            .ThenBy(monitor => monitor.Top)
            .ToList();
    }

    private static IReadOnlyDictionary<string, string> GetMonitorModelNameByDeviceName()
    {
        Dictionary<string, string> modelByDeviceName = new(StringComparer.OrdinalIgnoreCase);
        IReadOnlyDictionary<string, string> friendlyModelByHardwareId = GetFriendlyModelNameByHardwareId();
        DisplayDevice adapterDevice = DisplayDevice.Create();
        for (uint adapterIndex = 0; EnumDisplayDevices(null, adapterIndex, ref adapterDevice, 0); adapterIndex++)
        {
            if ((adapterDevice.StateFlags & DisplayDeviceAttachedToDesktop) == 0)
            {
                adapterDevice = DisplayDevice.Create();
                continue;
            }

            string adapterName = adapterDevice.DeviceName.TrimEnd('\0');
            string modelName = GetFirstMonitorModelName(adapterName, friendlyModelByHardwareId);
            if (!string.IsNullOrWhiteSpace(adapterName) && !string.IsNullOrWhiteSpace(modelName))
            {
                modelByDeviceName[adapterName] = modelName;
            }

            adapterDevice = DisplayDevice.Create();
        }

        return modelByDeviceName;
    }

    private static string GetFirstMonitorModelName(
        string adapterName,
        IReadOnlyDictionary<string, string> friendlyModelByHardwareId)
    {
        DisplayDevice monitorDevice = DisplayDevice.Create();
        string fallbackModelName = string.Empty;
        for (uint monitorIndex = 0; EnumDisplayDevices(adapterName, monitorIndex, ref monitorDevice, 0); monitorIndex++)
        {
            string candidateModelName = ResolveMonitorModelName(monitorDevice, friendlyModelByHardwareId);
            if (string.IsNullOrWhiteSpace(fallbackModelName))
            {
                fallbackModelName = candidateModelName;
            }

            monitorDevice = DisplayDevice.Create();
            if (!string.IsNullOrWhiteSpace(candidateModelName) &&
                !string.Equals(candidateModelName, "Generic PnP Monitor", StringComparison.OrdinalIgnoreCase))
            {
                return candidateModelName;
            }
        }

        return fallbackModelName;
    }

    private static string ResolveMonitorModelName(
        DisplayDevice monitorDevice,
        IReadOnlyDictionary<string, string> friendlyModelByHardwareId)
    {
        string hardwareId = ExtractMonitorHardwareId(monitorDevice.DeviceID);
        if (!string.IsNullOrWhiteSpace(hardwareId) &&
            friendlyModelByHardwareId.TryGetValue(hardwareId, out string? friendlyModelName) &&
            !string.IsNullOrWhiteSpace(friendlyModelName))
        {
            return friendlyModelName;
        }

        return monitorDevice.DeviceString.TrimEnd('\0').Trim();
    }

    private static IReadOnlyDictionary<string, string> GetFriendlyModelNameByHardwareId()
    {
        Dictionary<string, string> friendlyModelByHardwareId = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            using ManagementObjectSearcher searcher = new(
                "root\\wmi",
                "SELECT InstanceName, UserFriendlyName FROM WmiMonitorID WHERE Active = TRUE");
            foreach (ManagementObject monitor in searcher.Get().Cast<ManagementObject>())
            {
                string instanceName = monitor["InstanceName"]?.ToString() ?? string.Empty;
                string hardwareId = ExtractMonitorHardwareId(instanceName);
                string friendlyName = DecodeFriendlyName(monitor["UserFriendlyName"] as ushort[]);
                if (string.IsNullOrWhiteSpace(hardwareId) || string.IsNullOrWhiteSpace(friendlyName))
                {
                    continue;
                }

                friendlyModelByHardwareId[hardwareId] = friendlyName;
            }
        }
        catch (ManagementException exception)
        {
            AppLogger.LogException("Unable to read WmiMonitorID friendly names", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            AppLogger.LogException("Unauthorized reading WmiMonitorID friendly names", exception);
        }

        return friendlyModelByHardwareId;
    }

    private static string DecodeFriendlyName(ushort[]? values)
    {
        if (values is null || values.Length == 0)
        {
            return string.Empty;
        }

        StringBuilder builder = new();
        foreach (ushort value in values)
        {
            if (value == 0)
            {
                break;
            }

            builder.Append((char)value);
        }

        return builder.ToString().Trim();
    }

    private static string ExtractMonitorHardwareId(string rawDeviceId)
    {
        if (string.IsNullOrWhiteSpace(rawDeviceId))
        {
            return string.Empty;
        }

        string normalizedId = rawDeviceId.Trim();
        int monitorPrefix = normalizedId.IndexOf("MONITOR\\", StringComparison.OrdinalIgnoreCase);
        if (monitorPrefix >= 0)
        {
            normalizedId = normalizedId[(monitorPrefix + "MONITOR\\".Length)..];
        }
        else
        {
            int displayPrefix = normalizedId.IndexOf("DISPLAY\\", StringComparison.OrdinalIgnoreCase);
            if (displayPrefix >= 0)
            {
                normalizedId = normalizedId[(displayPrefix + "DISPLAY\\".Length)..];
            }
        }

        int separator = normalizedId.IndexOf('\\');
        return separator >= 0
            ? normalizedId[..separator]
            : normalizedId;
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

        ushort atom = RegisterClass(ref windowClass);
        if (atom == 0)
        {
            throw new InvalidOperationException($"RegisterClass failed with Win32 error {Marshal.GetLastWin32Error()}.");
        }

        _windowClassRegistered = true;
    }

    private static IntPtr HandleWindowMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (!ServiceByWindowHandle.TryGetValue(hwnd, out MonitorTopologyService? service))
        {
            return DefWindowProc(hwnd, message, wParam, lParam);
        }

        if (message is WmDisplayChange or WmDeviceChange)
        {
            service.InvalidateCache();
            return IntPtr.Zero;
        }

        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private const uint MonitorInfofPrimary = 0x00000001;
    private const uint DisplayDeviceAttachedToDesktop = 0x00000001;

    private delegate bool MonitorEnumerationDelegate(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

    private delegate IntPtr WindowProcedureDelegate(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc,
        IntPtr clipRect,
        MonitorEnumerationDelegate callback,
        IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitorHandle, ref MonitorInfoEx monitorInfo);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(
        string? deviceName,
        uint deviceNumber,
        ref DisplayDevice displayDevice,
        uint flags);

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
    private struct MonitorInfoEx
    {
        public int Size;
        public Rect MonitorArea;
        public Rect WorkArea;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        public static MonitorInfoEx Create()
        {
            return new MonitorInfoEx
            {
                Size = Marshal.SizeOf<MonitorInfoEx>(),
                DeviceName = string.Empty
            };
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public uint StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;

        public static DisplayDevice Create()
        {
            return new DisplayDevice
            {
                Size = Marshal.SizeOf<DisplayDevice>(),
                DeviceName = string.Empty,
                DeviceString = string.Empty,
                DeviceID = string.Empty,
                DeviceKey = string.Empty
            };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

internal sealed class MonitorDescriptor
{
    public string DeviceName { get; init; } = string.Empty;

    public string ModelName { get; init; } = string.Empty;

    public bool IsPrimary { get; init; }

    public int Left { get; init; }

    public int Top { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }
}
