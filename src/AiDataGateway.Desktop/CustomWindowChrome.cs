using System.Runtime.InteropServices;
using System.Windows;

namespace AiDataGateway.Desktop;

// WindowChrome owns drag, resize and caption hit-testing for the borderless
// window; this helper only keeps the DWM polish and the maximize work-area
// fix that WindowChrome does not provide on its own.
internal static class CustomWindowChrome
{
    private const int WmGetMinMaxInfo = 0x0024;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmWindowCornerPreferenceRound = 2;
    private const uint MonitorDefaultToNearest = 2;

    public static void ApplyVisuals(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return;
        }

        var corner = DwmWindowCornerPreferenceRound;
        TrySetDwmAttribute(windowHandle, DwmwaWindowCornerPreference, corner);
        const int borderColor = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
        TrySetDwmAttribute(windowHandle, DwmwaBorderColor, borderColor);
    }

    public static void ConstrainMaximizedBounds(IntPtr windowHandle, IntPtr minMaxInfoPointer)
    {
        var monitor = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return;
        }

        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        var minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(minMaxInfoPointer);
        minMaxInfo.MaxPosition.X = monitorInfo.WorkArea.Left - monitorInfo.MonitorArea.Left;
        minMaxInfo.MaxPosition.Y = monitorInfo.WorkArea.Top - monitorInfo.MonitorArea.Top;
        minMaxInfo.MaxSize.X = monitorInfo.WorkArea.Right - monitorInfo.WorkArea.Left;
        minMaxInfo.MaxSize.Y = monitorInfo.WorkArea.Bottom - monitorInfo.WorkArea.Top;
        Marshal.StructureToPtr(minMaxInfo, minMaxInfoPointer, false);
    }

    public static IntPtr HookMinMaxInfo(Window window)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        var source = System.Windows.Interop.HwndSource.FromHwnd(handle);
        source?.AddHook((windowHandle, message, wParam, lParam, ref handled) =>
        {
            if (message == WmGetMinMaxInfo)
            {
                ConstrainMaximizedBounds(windowHandle, lParam);
                handled = true;
            }
            return IntPtr.Zero;
        });
        return handle;
    }

    private static void TrySetDwmAttribute(IntPtr windowHandle, int attribute, int value)
    {
        try
        {
            _ = DwmSetWindowAttribute(windowHandle, attribute, ref value, sizeof(int));
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRectangle MonitorArea;
        public NativeRectangle WorkArea;
        public uint Flags;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr windowHandle, int attribute, ref int attributeValue, int attributeSize);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitorHandle, ref MonitorInfo monitorInfo);
}
