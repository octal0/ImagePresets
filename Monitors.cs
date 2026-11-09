using System.Runtime.InteropServices;

namespace ImagePresets;

public sealed record MonitorInfo(int Index, string Device, int X, int Y, int Width, int Height, bool Primary)
{
    public double Aspect => (double)Width / Height;

    public override string ToString() =>
        $"Monitor {Index + 1}{(Primary ? " (principal)" : "")} · {Width}×{Height}";
}

/// <summary>Lista de monitores (en píxeles físicos) y colocación de ventanas, con la API de Windows.</summary>
public static class Monitors
{
    public static List<MonitorInfo> GetAll()
    {
        var list = new List<MonitorInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr hdc, ref Rect r, IntPtr data) =>
        {
            var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
            if (GetMonitorInfo(hMon, ref info))
            {
                var b = info.Monitor;
                list.Add(new MonitorInfo(0, info.Device, b.Left, b.Top, b.Right - b.Left, b.Bottom - b.Top,
                    (info.Flags & 1) != 0));
            }
            return true;
        }, IntPtr.Zero);

        // De izquierda a derecha, como los ve el usuario
        return list.OrderBy(m => m.X).ThenBy(m => m.Y).Select((m, i) => m with { Index = i }).ToList();
    }

    public static void MoveWindow(IntPtr hwnd, MonitorInfo m) =>
        SetWindowPos(hwnd, IntPtr.Zero, m.X, m.Y, m.Width, m.Height, SwpNoZOrder | SwpNoActivate);

    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Device;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref Rect rect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfoEx info);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
