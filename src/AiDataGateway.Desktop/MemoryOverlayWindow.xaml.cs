using System.Runtime.InteropServices;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace AiDataGateway.Desktop;

public partial class MemoryOverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const double DragStartThreshold = 3;

    private static readonly Color Green = Color.FromRgb(0x2B, 0xBE, 0x81);
    private static readonly Color Amber = Color.FromRgb(0xF2, 0xA8, 0x2D);
    private static readonly Color Red = Color.FromRgb(0xE1, 0x4A, 0x55);

    private readonly DispatcherTimer _refreshTimer;
    private bool _dragging;
    private bool _dragMoved;
    private Point _dragOrigin;
    private Point _grabOffset;
    private Rect _dragWorkArea;
    private Point _lastClickSpot;
    private long _lastClickTimestamp;
    private int _clickCount;
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
        var rawAreas = ScreenInterop.GetWorkAreas().ToList();
        var fromDevice = DeviceTransformFromWindowToDips();
        var workAreas = rawAreas
            .Select(area =>
            {
                var origin = fromDevice.Transform(new Point(area.X, area.Y));
                var corner = fromDevice.Transform(new Point(area.Right, area.Bottom));
                return new Rect(origin, corner);
            })
            .ToList();
        var savedPoint = new Point(Left, Top);
        var home = workAreas.FirstOrDefault(area => ScreenInterop.Contains(area, savedPoint, (int)Math.Ceiling(Width), (int)Math.Ceiling(Height)));
        // default(Rect) is (0,0,0,0) and WPF's IsEmpty only covers Rect.Empty,
        // so the fallback must check for a zero-sized rect explicitly.
        if (home.Width <= 0 || home.Height <= 0 || home.IsEmpty)
        {
            home = workAreas.Count > 0 ? workAreas[0] : SystemParameters.WorkArea;
        }
        var leftValue = ScreenInterop.Clamp(savedPoint.X < 0 ? home.Right - Width - 24 : savedPoint.X, home.Left, home.Right - Width);
        var topValue = ScreenInterop.Clamp(savedPoint.Y < 0 ? home.Bottom - Height - 24 : savedPoint.Y, home.Top, home.Bottom - Height);
        Left = leftValue;
        Top = topValue;
    }

    // Monitor rectangles come back in physical pixels; WPF positions windows in
    // device-independent units, so every screen calculation goes through the
    // per-window device transform (a mismatch used to park the ball off-screen).
    private List<Rect> GetWorkAreasInDips()
    {
        var fromDevice = DeviceTransformFromWindowToDips();
        return ScreenInterop.GetWorkAreas()
            .Select(area =>
            {
                var origin = fromDevice.Transform(new Point(area.X, area.Y));
                var corner = fromDevice.Transform(new Point(area.Right, area.Bottom));
                return new Rect(origin, corner);
            })
            .ToList();
    }

    private Matrix DeviceTransformFromWindowToDips()
    {
        return (PresentationSource.FromVisual(this) as System.Windows.Interop.HwndSource)
            ?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
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
        var radius = (Width - 12) / 2;
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

        // Distinguish drag from double-click by movement instead of ClickCount:
        // the system counts two quick presses as a double-click even when the
        // user is just re-grabbing the ball to continue a drag, which used to
        // open the console instead of moving it.
        var cursor = ScreenToDip(GetCursorPhysical());
        var timestamp = Environment.TickCount64;
        var withinDoubleClick = timestamp - _lastClickTimestamp <= GetDoubleClickTime()
            && Distance(cursor, _lastClickSpot) < 12;
        _clickCount = withinDoubleClick ? _clickCount + 1 : 1;
        _lastClickTimestamp = timestamp;
        _lastClickSpot = cursor;

        _dragging = true;
        _dragMoved = false;
        _dragOrigin = new Point(Left, Top);
        _grabOffset = new Point(cursor.X - Left, cursor.Y - Top);
        _dragWorkArea = PickWorkArea(cursor);
        Root.CaptureMouse();
    }

    private void OnDragMove(object sender, MouseEventArgs eventArgs)
    {
        if (!_dragging) return;
        // Absolute cursor position (never window-relative): deriving it from
        // GetPosition on a moving window feeds the motion back into itself
        // and makes the ball trail or jump behind the pointer.
        var cursor = ScreenToDip(GetCursorPhysical());
        if (!_dragMoved)
        {
            if (Distance(cursor, _lastClickSpot) < DragStartThreshold) return;
            _dragMoved = true;
        }

        var proposed = new Point(cursor.X - _grabOffset.X, cursor.Y - _grabOffset.Y);
        Left = ScreenInterop.Clamp(proposed.X, _dragWorkArea.Left, _dragWorkArea.Right - Width);
        Top = ScreenInterop.Clamp(proposed.Y, _dragWorkArea.Top, _dragWorkArea.Bottom - Height);
    }

    private void OnDragEnd(object sender, MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ChangedButton != MouseButton.Left || !_dragging) return;
        _dragging = false;
        if (Root.IsMouseCaptured) Root.ReleaseMouseCapture();

        if (!_dragMoved)
        {
            // A press without movement is part of a click; a second one opens
            // the console. Restore the exact pre-press spot either way.
            Left = _dragOrigin.X;
            Top = _dragOrigin.Y;
            if (_clickCount >= 2) OpenConsoleRequested?.Invoke();
            return;
        }

        PositionCommitted?.Invoke(new Point(Left, Top));
    }

    private Rect PickWorkArea(Point cursor)
    {
        var area = GetWorkAreasInDips().FirstOrDefault(candidate => ScreenInterop.ContainsPoint(candidate, cursor));
        if (area.Width <= 0 || area.Height <= 0 || area.IsEmpty) area = SystemParameters.WorkArea;
        return area;
    }

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private Point ScreenToDip(Point physical)
    {
        return DeviceTransformFromWindowToDips().Transform(physical);
    }

    private static Point GetCursorPhysical()
    {
        if (!GetCursorPos(out var point)) return new Point();
        return new Point(point.X, point.Y);
    }

    private void OnOpenConsoleClick(object sender, RoutedEventArgs eventArgs) => OpenConsoleRequested?.Invoke();

    private void OnDisableClick(object sender, RoutedEventArgs eventArgs) => DisableRequested?.Invoke();

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out CursorPoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint
    {
        public int X;
        public int Y;
    }

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
