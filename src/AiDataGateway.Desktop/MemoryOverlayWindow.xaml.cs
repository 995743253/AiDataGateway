using System.Runtime.InteropServices;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace AiDataGateway.Desktop;

public partial class MemoryOverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    private static readonly Color Green = Color.FromRgb(0x2B, 0xBE, 0x81);
    private static readonly Color Amber = Color.FromRgb(0xF2, 0xA8, 0x2D);
    private static readonly Color Red = Color.FromRgb(0xE1, 0x4A, 0x55);

    private readonly DispatcherTimer _refreshTimer;
    private bool _dragging;
    private Point _dragStartPointer;
    private Point _dragStartLocation;
    private int _memoryPercent;

    public event Action<Point>? PositionCommitted;
    public event Action? OpenConsoleRequested;
    public event Action? DisableRequested;

    public MemoryOverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            ApplyToolWindowStyle();
            // The device transform only exists once the handle is created, so
            // restoring the saved position before Show() would clamp against
            // raw physical pixels and park the ball off-screen.
            RestoreSavedPosition();
        };
        DrawRing(0);
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _refreshTimer.Tick += (_, _) => RefreshMemoryUsage();
        Loaded += (_, _) =>
        {
            RefreshMemoryUsage();
            _refreshTimer.Start();
        };
        Closed += (_, _) => _refreshTimer.Stop();
    }

    public void RestoreSavedPosition()
    {
        var workAreas = GetWorkAreasInDips();
        var saved = new Point(Left, Top);
        var home = workAreas.FirstOrDefault(area => ScreenInterop.Contains(area, saved, (int)Width, (int)Height));
        if (home.IsEmpty) home = workAreas.Count > 0 ? workAreas[0] : SystemParameters.WorkArea;
        Left = ScreenInterop.Clamp(saved.X < 0 ? home.Right - Width - 24 : saved.X, home.Left, home.Right - Width);
        Top = ScreenInterop.Clamp(saved.Y < 0 ? home.Bottom - Height - 24 : saved.Y, home.Top, home.Bottom - Height);
    }

    // Monitor rectangles come back in physical pixels; WPF positions windows in
    // device-independent units, so every screen calculation goes through the
    // per-window device transform (a mismatch used to park the ball off-screen).
    private List<Rect> GetWorkAreasInDips()
    {
        var fromDevice = (PresentationSource.FromVisual(this) as System.Windows.Interop.HwndSource)
            ?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        return ScreenInterop.GetWorkAreas()
            .Select(area =>
            {
                var origin = fromDevice.Transform(new Point(area.X, area.Y));
                var corner = fromDevice.Transform(new Point(area.Right, area.Bottom));
                return new Rect(origin, corner);
            })
            .ToList();
    }

    private void ApplyToolWindowStyle()
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        _ = ScreenInterop.SetWindowLong(handle, GwlExStyle, ScreenInterop.GetWindowLong(handle, GwlExStyle) | WsExToolWindow | WsExNoActivate);
    }

    private void RefreshMemoryUsage()
    {
        var status = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(ref status) || status.TotalPhysical == 0) return;

        var usedBytes = status.TotalPhysical - status.AvailablePhysical;
        _memoryPercent = Math.Clamp((int)Math.Round(usedBytes * 100D / status.TotalPhysical), 0, 100);
        var usedGigabytes = usedBytes / 1024D / 1024D / 1024D;
        var totalGigabytes = status.TotalPhysical / 1024D / 1024D / 1024D;

        PercentText.Text = $"{_memoryPercent}%";
        DetailText.Text = totalGigabytes > 0 ? $"{usedGigabytes:0.0}/{totalGigabytes:0.0} GB" : "正在读取";
        UsageRing.Stroke = new SolidColorBrush(_memoryPercent >= 90 ? Red : _memoryPercent >= 75 ? Amber : Green);
        ToolTip = $"内存使用率 {_memoryPercent}%\n已用 {usedGigabytes:0.0} GB / {totalGigabytes:0.0} GB\n双击打开 AiDataGateway";
        DrawRing(_memoryPercent);
    }

    private void DrawRing(int percent)
    {
        var center = new Point(Width / 2, Height / 2);
        var radius = (Width - 17) / 2;
        var start = new Point(center.X, center.Y - radius);

        TrackRing.Data = CirclePath(start, radius, 359.99);
        var sweep = Math.Clamp(percent * 3.6, 2, 359.99);
        UsageRing.Data = CirclePath(start, radius, sweep);
    }

    private static Geometry CirclePath(Point start, double radius, double sweepDegrees)
    {
        var radians = sweepDegrees * Math.PI / 180;
        var end = new Point(
            start.X + radius * Math.Sin(radians),
            start.Y + radius - radius * Math.Cos(radians));
        var arc = new ArcSegment(end, new Size(radius, radius), 0, sweepDegrees > 180, SweepDirection.Clockwise, true);
        var segments = new PathSegmentCollection { arc };
        var figure = new PathFigure(start, segments, false);
        return new PathGeometry([figure]);
    }

    private void OnDragBegin(object sender, MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ChangedButton != MouseButton.Left) return;
        if (eventArgs.ClickCount >= 2)
        {
            _dragging = false;
            OpenConsoleRequested?.Invoke();
            return;
        }

        _dragging = true;
        _dragStartPointer = ScreenToDip(PointToScreen(eventArgs.GetPosition(this)));
        _dragStartLocation = new Point(Left, Top);
        CaptureMouse();
    }

    private void OnDragMove(object sender, MouseEventArgs eventArgs)
    {
        if (!_dragging) return;
        var pointer = ScreenToDip(PointToScreen(eventArgs.GetPosition(this)));
        var proposed = _dragStartLocation + (pointer - _dragStartPointer);
        var workArea = GetWorkAreasInDips().FirstOrDefault(area => ScreenInterop.ContainsPoint(area, pointer));
        if (workArea.IsEmpty) workArea = SystemParameters.WorkArea;
        Left = ScreenInterop.Clamp(proposed.X, workArea.Left, workArea.Right - Width);
        Top = ScreenInterop.Clamp(proposed.Y, workArea.Top, workArea.Bottom - Height);
    }

    private Point ScreenToDip(Point physical)
    {
        var fromDevice = (PresentationSource.FromVisual(this) as System.Windows.Interop.HwndSource)
            ?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        return fromDevice.Transform(physical);
    }

    private void OnDragEnd(object sender, MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ChangedButton != MouseButton.Left || !_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        PositionCommitted?.Invoke(new Point(Left, Top));
    }

    private void OnOpenConsoleClick(object sender, RoutedEventArgs eventArgs) => OpenConsoleRequested?.Invoke();

    private void OnDisableClick(object sender, RoutedEventArgs eventArgs) => DisableRequested?.Invoke();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;

        public MemoryStatusEx()
        {
            Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        }
    }

    private static class ScreenInterop
    {
        public static IEnumerable<Rect> GetWorkAreas()
        {
            var areas = new List<Rect>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, hdc, rect, data) =>
            {
                var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
                if (GetMonitorInfo(monitor, ref info)) areas.Add(new Rect(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top));
                return true;
            }, IntPtr.Zero);
            return areas;
        }

        public static bool Contains(Rect area, Point location, int width, int height) =>
            area.IntersectsWith(new Rect(location.X, location.Y, width, height));

        public static bool ContainsPoint(Rect area, Point point) => area.Contains(point);

        public static double Clamp(double value, double minimum, double maximum) =>
            Math.Clamp(value, Math.Min(minimum, maximum), Math.Max(minimum, maximum));

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRectangle
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MonitorInfoEx
        {
            public int Size;
            public NativeRectangle Monitor;
            public NativeRectangle Work;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string Device;
        }

        private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern int GetWindowLong(IntPtr windowHandle, int index);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern int SetWindowLong(IntPtr windowHandle, int index, int value);
    }
}
