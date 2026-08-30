using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace AiDataGateway.Desktop;

internal sealed class MemoryUsageOverlayForm : Form
{
    private static readonly Color Navy = Color.FromArgb(13, 35, 62);
    private static readonly Color Blue = Color.FromArgb(30, 132, 191);
    private static readonly Color Green = Color.FromArgb(43, 190, 129);
    private static readonly Color Amber = Color.FromArgb(242, 168, 45);
    private static readonly Color Red = Color.FromArgb(225, 74, 85);

    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly ContextMenuStrip _menu;
    private readonly ToolTip _toolTip = new();
    private bool _dragging;
    private Point _dragStartPointer;
    private Point _dragStartLocation;
    private int _memoryPercent;
    private double _usedGigabytes;
    private double _totalGigabytes;

    public event Action<Point>? PositionCommitted;
    public event Action? OpenConsoleRequested;
    public event Action? DisableRequested;

    public MemoryUsageOverlayForm()
    {
        Text = "内存使用率";
        Name = "MemoryUsageOverlay";
        AccessibleName = "内存使用率悬浮球";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(116, 116);
        MinimumSize = Size;
        MaximumSize = Size;
        BackColor = Navy;
        AutoScaleMode = AutoScaleMode.None;
        Cursor = Cursors.SizeAll;
        DoubleBuffered = true;

        _menu = new ContextMenuStrip
        {
            Font = new Font("Microsoft YaHei UI", 9F),
            ShowImageMargin = false
        };
        _menu.Items.Add("打开控制台", null, (_, _) => OpenConsoleRequested?.Invoke());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("关闭内存悬浮球", null, (_, _) => DisableRequested?.Invoke());
        ContextMenuStrip = _menu;

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 2_000 };
        _refreshTimer.Tick += (_, _) => RefreshMemoryUsage();
        Shown += (_, _) =>
        {
            RefreshMemoryUsage();
            _refreshTimer.Start();
        };
        MouseDoubleClick += (_, eventArgs) =>
        {
            if (eventArgs.Button == MouseButtons.Left) OpenConsoleRequested?.Invoke();
        };
        MouseDown += BeginDrag;
        MouseMove += ContinueDrag;
        MouseUp += EndDrag;
        Resize += (_, _) => UpdateRoundRegion();
        UpdateRoundRegion();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int wsExToolWindow = 0x00000080;
            var parameters = base.CreateParams;
            parameters.ExStyle |= wsExToolWindow;
            return parameters;
        }
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        var graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var backgroundBounds = new Rectangle(1, 1, Width - 3, Height - 3);
        using (var background = new LinearGradientBrush(backgroundBounds, Navy, Color.FromArgb(20, 91, 139), 45F))
        {
            graphics.FillEllipse(background, backgroundBounds);
        }

        var ringBounds = new Rectangle(8, 8, Width - 17, Height - 17);
        using (var track = new Pen(Color.FromArgb(58, 255, 255, 255), 6.5F))
        {
            graphics.DrawArc(track, ringBounds, 0F, 360F);
        }

        var usageColor = _memoryPercent >= 90 ? Red : _memoryPercent >= 75 ? Amber : Green;
        using (var usage = new Pen(usageColor, 6.5F) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            graphics.DrawArc(usage, ringBounds, -90F, Math.Max(2F, _memoryPercent * 3.6F));
        }

        using var percentFont = new Font("Segoe UI Semibold", 20F, FontStyle.Bold, GraphicsUnit.Pixel);
        using var titleFont = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Pixel);
        using var detailFont = new Font("Segoe UI", 8F, FontStyle.Regular, GraphicsUnit.Pixel);
        TextRenderer.DrawText(
            graphics,
            $"{_memoryPercent}%",
            percentFont,
            new Rectangle(8, 29, Width - 16, 32),
            Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(
            graphics,
            "内存使用",
            titleFont,
            new Rectangle(8, 59, Width - 16, 18),
            Color.FromArgb(218, 236, 249),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        var detail = _totalGigabytes > 0 ? $"{_usedGigabytes:0.0}/{_totalGigabytes:0.0} GB" : "正在读取";
        TextRenderer.DrawText(
            graphics,
            detail,
            detailFont,
            new Rectangle(8, 78, Width - 16, 16),
            Color.FromArgb(178, 207, 227),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private void RefreshMemoryUsage()
    {
        var status = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(ref status) || status.TotalPhysical == 0) return;

        var usedBytes = status.TotalPhysical - status.AvailablePhysical;
        _memoryPercent = Math.Clamp((int)Math.Round(usedBytes * 100D / status.TotalPhysical), 0, 100);
        _usedGigabytes = usedBytes / 1024D / 1024D / 1024D;
        _totalGigabytes = status.TotalPhysical / 1024D / 1024D / 1024D;
        _toolTip.SetToolTip(this, $"内存使用率 {_memoryPercent}%\n已用 {_usedGigabytes:0.0} GB / {_totalGigabytes:0.0} GB\n双击打开 AiDataGateway");
        Invalidate();
    }

    private void BeginDrag(object? sender, MouseEventArgs eventArgs)
    {
        if (eventArgs.Button != MouseButtons.Left) return;
        _dragging = true;
        _dragStartPointer = Cursor.Position;
        _dragStartLocation = Location;
        Capture = true;
    }

    private void ContinueDrag(object? sender, MouseEventArgs eventArgs)
    {
        if (!_dragging) return;
        var delta = new Size(Cursor.Position.X - _dragStartPointer.X, Cursor.Position.Y - _dragStartPointer.Y);
        var proposed = _dragStartLocation + delta;
        var workingArea = Screen.FromPoint(Cursor.Position).WorkingArea;
        proposed.X = Math.Clamp(proposed.X, workingArea.Left, Math.Max(workingArea.Left, workingArea.Right - Width));
        proposed.Y = Math.Clamp(proposed.Y, workingArea.Top, Math.Max(workingArea.Top, workingArea.Bottom - Height));
        Location = proposed;
    }

    private void EndDrag(object? sender, MouseEventArgs eventArgs)
    {
        if (eventArgs.Button != MouseButtons.Left || !_dragging) return;
        _dragging = false;
        Capture = false;
        PositionCommitted?.Invoke(Location);
    }

    private void UpdateRoundRegion()
    {
        using var path = new GraphicsPath();
        path.AddEllipse(ClientRectangle);
        var oldRegion = Region;
        Region = new Region(path);
        oldRegion?.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Dispose();
            _menu.Dispose();
            _toolTip.Dispose();
            Region?.Dispose();
        }

        base.Dispose(disposing);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
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
}
