using System.Runtime.InteropServices;

namespace NativeScreenDimmer_WinUI3.Services;

internal sealed class MonitorTopologyService
{
    public IReadOnlyList<MonitorDescriptor> GetMonitors()
    {
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

            monitors.Add(new MonitorDescriptor
            {
                DeviceName = monitorInfo.DeviceName.TrimEnd('\0'),
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

    private const uint MonitorInfofPrimary = 0x00000001;

    private delegate bool MonitorEnumerationDelegate(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc,
        IntPtr clipRect,
        MonitorEnumerationDelegate callback,
        IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitorHandle, ref MonitorInfoEx monitorInfo);

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

    public bool IsPrimary { get; init; }

    public int Left { get; init; }

    public int Top { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }
}
