using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace AiDataGateway.Desktop;

internal sealed class GatewayMainForm : Form
{
    private const int WmGetMinMaxInfo = 0x0024;
    private const int WmNcHitTest = 0x0084;
    private const int ResizeBorder = 5;
    private const int HtClient = 1;
    private const int HtCaption = 2;

    private static readonly Color Navy = Color.FromArgb(12, 31, 58);
    private static readonly Color Green = Color.FromArgb(31, 178, 120);
    private static readonly Color Amber = Color.FromArgb(242, 168, 45);
    private static readonly Color Red = Color.FromArgb(225, 74, 85);

    private readonly Uri _baseAddress;
    private readonly DesktopSettingsStore _desktopSettingsStore;
    private DesktopSettings _desktopSettings;
    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.White };
    private readonly Label _status = new() { AutoSize = true, ForeColor = Color.FromArgb(55, 71, 92), Text = "正在启动本地网关…" };
    private readonly Label _runtime = new() { AutoSize = true, ForeColor = Color.FromArgb(107, 119, 138) };
    private readonly Panel _statusDot = new() { Size = new Size(9, 9), BackColor = Amber, Margin = new Padding(2, 6, 9, 0) };
    private readonly Label _loadingTitle = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Text = "正在建立安全通道", Font = new Font("Segoe UI Semibold", 17F), ForeColor = Navy };
    private readonly Label _loadingMessage = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Text = "正在连接本地 API 与受保护的数据访问工作区…", Font = new Font("Segoe UI", 9.5F), ForeColor = Color.FromArgb(103, 117, 139) };
    private readonly Image _brandImage;
    private readonly Image _smallImage;
    private readonly Icon _appIcon;
    private readonly NotifyIcon _trayIcon;
    private readonly ContextMenuStrip _trayMenu;
    private readonly Panel _loadingOverlay;
    private ToolStripMenuItem _memoryOverlayMenuItem = null!;
    private MemoryUsageOverlayForm? _memoryOverlay;
    private Panel _titleBar = null!;
    private CaptionButton _minimizeButton = null!;
    private CaptionButton _maximizeButton = null!;
    private CaptionButton _closeButton = null!;
    private ResizeEdges _activeResizeEdges;
    private Point _resizeStartPointer;
    private Rectangle _resizeStartBounds;
    private bool _manualResizing;
    private bool _allowExit;

    public GatewayMainForm(Uri baseAddress, string storagePath)
    {
        _baseAddress = baseAddress;
        _desktopSettingsStore = new DesktopSettingsStore(storagePath);
        _desktopSettings = _desktopSettingsStore.Load();
        _brandImage = LoadEmbeddedImage("AiDataGateway.Desktop.Assets.gateway-brand-large.png");
        _smallImage = LoadEmbeddedImage("AiDataGateway.Desktop.Assets.gateway-app-icon.png");
        _appIcon = Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath) ?? (Icon)SystemIcons.Shield.Clone();

        Text = "AiDataGateway · 本地 AI 数据安全网关";
        Icon = _appIcon;
        Width = 1380;
        Height = 880;
        MinimumSize = new Size(1024, 680);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        Padding = new Padding(ResizeBorder);
        BackColor = Color.FromArgb(15, 39, 69);
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;

        _titleBar = CreateTitleBar();
        var footer = CreateFooter();
        var contentFrame = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White
        };
        contentFrame.Controls.Add(_webView);
        _loadingOverlay = CreateLoadingOverlay();
        contentFrame.Controls.Add(_loadingOverlay);
        _loadingOverlay.BringToFront();

        Controls.Add(contentFrame);
        Controls.Add(footer);
        Controls.Add(_titleBar);

        _trayMenu = CreateTrayMenu();
        _trayIcon = new NotifyIcon
        {
            Icon = _appIcon,
            Text = "AiDataGateway · 本地 AI 数据安全网关",
            Visible = true,
            ContextMenuStrip = _trayMenu
        };
        _trayIcon.DoubleClick += (_, _) => RestoreWindow();

        Shown += async (_, _) =>
        {
            ApplyMemoryOverlaySetting();
            await InitializeWebViewAsync();
        };
        Resize += (_, _) =>
        {
            UpdateWindowStateAppearance();
            if (WindowState == FormWindowState.Minimized)
            {
                HideToTray(showTip: false);
            }
        };
        FormClosing += OnFormClosing;
        KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode == Keys.F5)
            {
                ReloadWebView();
                eventArgs.Handled = true;
            }
            else if (eventArgs.Control && eventArgs.Shift && eventArgs.KeyCode == Keys.O)
            {
                OpenInBrowser();
                eventArgs.Handled = true;
            }
        };
    }

    private Panel CreateTitleBar()
    {
        var titleBar = new GradientTitleBarPanel
        {
            Dock = DockStyle.Top,
            Height = 44,
            IconImage = _smallImage
        };
        _minimizeButton = CreateCaptionButton("\uE921", (_, _) => WindowState = FormWindowState.Minimized);
        _maximizeButton = CreateCaptionButton("\uE922", (_, _) => ToggleMaximized());
        _closeButton = CreateCaptionButton("\uE8BB", (_, _) => Close(), closeButton: true);
        _minimizeButton.Dock = DockStyle.Right;
        _maximizeButton.Dock = DockStyle.Right;
        _closeButton.Dock = DockStyle.Right;
        titleBar.Controls.Add(_minimizeButton);
        titleBar.Controls.Add(_maximizeButton);
        titleBar.Controls.Add(_closeButton);
        EnableWindowDrag(titleBar);
        return titleBar;
    }

    private static CaptionButton CreateCaptionButton(string glyph, EventHandler click, bool closeButton = false)
    {
        var button = new CaptionButton(closeButton)
        {
            Text = glyph,
            Size = new Size(46, 44),
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.Transparent,
            ForeColor = Color.White,
            Font = new Font("Segoe MDL2 Assets", 9.5F),
            Cursor = Cursors.Default,
            TabStop = false
        };
        button.Click += click;
        return button;
    }

    private void EnableWindowDrag(Control control)
    {
        control.MouseDown += (_, eventArgs) =>
        {
            if (eventArgs.Button == MouseButtons.Left)
            {
                CustomWindowChrome.BeginWindowDrag(Handle);
            }
        };
        control.MouseDoubleClick += (_, eventArgs) =>
        {
            if (eventArgs.Button == MouseButtons.Left)
            {
                ToggleMaximized();
            }
        };

    }

    private Control CreateFooter()
    {
        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            Padding = new Padding(14, 5, 10, 5),
            BackColor = Color.FromArgb(248, 250, 253)
        };
        footer.Paint += (_, eventArgs) =>
        {
            using var pen = new Pen(Color.FromArgb(215, 223, 234));
            eventArgs.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var statusArea = new FlowLayoutPanel
        {
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            WrapContents = false,
            BackColor = Color.Transparent
        };
        statusArea.Controls.Add(_statusDot);
        statusArea.Controls.Add(_status);

        var actions = new FlowLayoutPanel
        {
            Anchor = AnchorStyles.Right,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = Padding.Empty
        };
        actions.Controls.Add(CreateActionButton("刷新", (_, _) => ReloadWebView(), accent: true));
        actions.Controls.Add(CreateActionButton("浏览器打开", (_, _) => OpenInBrowser()));
        actions.Controls.Add(CreateActionButton("隐藏", (_, _) => HideToTray(showTip: true)));

        _runtime.Text = $"本地端点  {_baseAddress}";
        _runtime.Dock = DockStyle.Fill;
        _runtime.TextAlign = ContentAlignment.MiddleRight;
        _runtime.Padding = new Padding(0, 0, 14, 0);
        layout.Controls.Add(statusArea, 0, 0);
        layout.Controls.Add(_runtime, 1, 0);
        layout.Controls.Add(actions, 2, 0);
        footer.Controls.Add(layout);
        return footer;
    }

    private Panel CreateLoadingOverlay()
    {
        var overlay = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(241, 246, 253) };
        var card = new BrandCardPanel { Size = new Size(480, 365) };
        var logo = new PictureBox
        {
            Image = _brandImage,
            SizeMode = PictureBoxSizeMode.Zoom,
            Location = new Point(170, 35),
            Size = new Size(140, 140),
            BackColor = Color.Transparent
        };
        _loadingTitle.SetBounds(30, 190, 420, 42);
        _loadingMessage.SetBounds(45, 235, 390, 50);
        var progress = new ProgressBar
        {
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 28,
            Location = new Point(92, 309),
            Size = new Size(296, 5)
        };
        card.Controls.Add(logo);
        card.Controls.Add(_loadingTitle);
        card.Controls.Add(_loadingMessage);
        card.Controls.Add(progress);
        overlay.Controls.Add(card);

        void CenterCard() => card.Location = new Point(
            Math.Max(12, (overlay.ClientSize.Width - card.Width) / 2),
            Math.Max(12, (overlay.ClientSize.Height - card.Height) / 2));
        overlay.Resize += (_, _) => CenterCard();
        overlay.HandleCreated += (_, _) => CenterCard();
        return overlay;
    }

    private ContextMenuStrip CreateTrayMenu()
    {
        var menu = new ContextMenuStrip
        {
            Font = new Font("Segoe UI", 9.5F),
            ShowImageMargin = true,
            Padding = new Padding(4),
            Renderer = new ToolStripProfessionalRenderer(new GatewayMenuColors())
        };
        menu.Items.Add(new ToolStripMenuItem("打开控制台", new Bitmap(_smallImage, 20, 20), (_, _) => RestoreWindow()) { Font = new Font("Segoe UI Semibold", 9.5F) });
        menu.Items.Add("刷新页面", null, (_, _) => ReloadWebView());
        menu.Items.Add("使用浏览器打开", null, (_, _) => OpenInBrowser());
        menu.Items.Add(new ToolStripSeparator());
        _memoryOverlayMenuItem = new ToolStripMenuItem("显示内存悬浮球", null, (_, _) => SetMemoryOverlayEnabled(!_desktopSettings.MemoryOverlayEnabled))
        {
            Checked = _desktopSettings.MemoryOverlayEnabled
        };
        menu.Items.Add(_memoryOverlayMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出 AiDataGateway", null, (_, _) => ExitApplication());
        return menu;
    }

    private static Button CreateActionButton(string text, EventHandler click, bool accent = false)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = false,
            Size = new Size(text.Length > 4 ? 92 : 64, 32),
            Margin = new Padding(7, 0, 0, 0),
            Cursor = Cursors.Hand,
            FlatStyle = FlatStyle.Flat,
            BackColor = accent ? Color.FromArgb(30, 153, 205) : Color.White,
            ForeColor = accent ? Color.White : Color.FromArgb(50, 68, 91),
            Font = new Font("Segoe UI", 8.8F),
            TabStop = false
        };
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = accent ? Color.FromArgb(30, 153, 205) : Color.FromArgb(205, 215, 227);
        button.FlatAppearance.MouseOverBackColor = accent ? Color.FromArgb(38, 169, 218) : Color.FromArgb(238, 244, 250);
        button.FlatAppearance.MouseDownBackColor = accent ? Color.FromArgb(22, 133, 180) : Color.FromArgb(225, 234, 244);
        button.Click += click;
        return button;
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            SetStatus("正在初始化安全浏览器…", Amber);
            var webViewData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiDataGateway", "WebView2");
            var bundledRuntime = GetBundledWebView2Runtime();
            if (bundledRuntime is not null)
            {
                EnsureBundledRuntimePermissions(bundledRuntime, webViewData);
            }

            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: bundledRuntime,
                userDataFolder: webViewData);
            await _webView.EnsureCoreWebView2Async(environment);
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            _webView.CoreWebView2.NavigationStarting += (_, _) => SetStatus("正在加载管理控制台…", Amber);
            _webView.CoreWebView2.NavigationCompleted += (_, eventArgs) =>
            {
                if (eventArgs.IsSuccess)
                {
                    _loadingOverlay.Visible = false;
                    _webView.Focus();
                    SetStatus("安全网关运行中", Green);
                    SendDesktopState();
                }
                else
                {
                    _loadingTitle.Text = "控制台加载失败";
                    _loadingMessage.Text = $"WebView2 错误：{eventArgs.WebErrorStatus}";
                    SetStatus("控制台加载失败", Red);
                }
            };
            _runtime.Text = bundledRuntime is null
                ? $"{_baseAddress}  ·  系统 WebView2"
                : $"{_baseAddress}  ·  内置 WebView2";
            _webView.Source = _baseAddress;
        }
        catch (Exception exception)
        {
            _loadingTitle.Text = "WebView2 初始化失败";
            _loadingMessage.Text = "请检查运行环境后重试，或使用右上角按钮在浏览器中打开。";
            SetStatus("WebView2 初始化失败", Red);
            MessageBox.Show(exception.Message, "AiDataGateway · 初始化失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SetStatus(string text, Color color)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetStatus(text, color));
            return;
        }

        _status.Text = text;
        _statusDot.BackColor = color;
    }

    private void ReloadWebView()
    {
        if (_webView.CoreWebView2 is null)
        {
            return;
        }

        _webView.CoreWebView2.Reload();
        RestoreWindow();
    }

    private void OpenInBrowser()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_baseAddress.ToString()) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "无法打开浏览器", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs eventArgs)
    {
        if (!IsTrustedWebMessageSource(eventArgs.Source)) return;

        try
        {
            using var message = JsonDocument.Parse(eventArgs.WebMessageAsJson);
            if (!message.RootElement.TryGetProperty("type", out var typeProperty)) return;
            switch (typeProperty.GetString())
            {
                case "desktop.getState":
                    SendDesktopState();
                    break;
                case "desktop.memoryOverlay.set" when message.RootElement.TryGetProperty("enabled", out var enabledProperty):
                    SetMemoryOverlayEnabled(enabledProperty.GetBoolean());
                    break;
            }
        }
        catch (JsonException)
        {
            // Ignore malformed messages from page scripts.
        }
        catch (InvalidOperationException)
        {
            // Ignore messages with an unexpected JSON shape.
        }
    }

    private bool IsTrustedWebMessageSource(string source)
    {
        return Uri.TryCreate(source, UriKind.Absolute, out var sourceUri)
            && string.Equals(sourceUri.Scheme, _baseAddress.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(sourceUri.Host, _baseAddress.Host, StringComparison.OrdinalIgnoreCase)
            && sourceUri.Port == _baseAddress.Port;
    }

    private void SendDesktopState()
    {
        _webView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            type = "desktop.state",
            available = true,
            memoryOverlayEnabled = _desktopSettings.MemoryOverlayEnabled
        }));
    }

    private void SetMemoryOverlayEnabled(bool enabled)
    {
        _desktopSettings = _desktopSettings with { MemoryOverlayEnabled = enabled };
        SaveDesktopSettings();
        ApplyMemoryOverlaySetting();
        SendDesktopState();
    }

    private void ApplyMemoryOverlaySetting()
    {
        _memoryOverlayMenuItem.Checked = _desktopSettings.MemoryOverlayEnabled;
        if (!_desktopSettings.MemoryOverlayEnabled)
        {
            if (_memoryOverlay is not null)
            {
                _memoryOverlay.Close();
                _memoryOverlay.Dispose();
                _memoryOverlay = null;
            }
            return;
        }

        if (_memoryOverlay is { IsDisposed: false })
        {
            if (!_memoryOverlay.Visible) _memoryOverlay.Show();
            return;
        }

        _memoryOverlay = new MemoryUsageOverlayForm
        {
            Location = ResolveMemoryOverlayLocation()
        };
        _memoryOverlay.PositionCommitted += location =>
        {
            _desktopSettings = _desktopSettings with { MemoryOverlayX = location.X, MemoryOverlayY = location.Y };
            SaveDesktopSettings();
        };
        _memoryOverlay.OpenConsoleRequested += RestoreWindow;
        _memoryOverlay.DisableRequested += () => SetMemoryOverlayEnabled(false);
        _memoryOverlay.Show();
    }

    private Point ResolveMemoryOverlayLocation()
    {
        var saved = new Point(_desktopSettings.MemoryOverlayX ?? int.MinValue, _desktopSettings.MemoryOverlayY ?? int.MinValue);
        var overlayBounds = new Rectangle(saved, new Size(116, 116));
        var savedScreen = Screen.AllScreens.FirstOrDefault(screen => screen.WorkingArea.IntersectsWith(overlayBounds));
        if (savedScreen is not null)
        {
            return new Point(
                Math.Clamp(saved.X, savedScreen.WorkingArea.Left, Math.Max(savedScreen.WorkingArea.Left, savedScreen.WorkingArea.Right - overlayBounds.Width)),
                Math.Clamp(saved.Y, savedScreen.WorkingArea.Top, Math.Max(savedScreen.WorkingArea.Top, savedScreen.WorkingArea.Bottom - overlayBounds.Height)));
        }

        var workingArea = Screen.PrimaryScreen?.WorkingArea ?? SystemInformation.WorkingArea;
        return new Point(workingArea.Right - 140, workingArea.Bottom - 140);
    }

    private void SaveDesktopSettings()
    {
        try
        {
            _desktopSettingsStore.Save(_desktopSettings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _trayIcon.ShowBalloonTip(2_000, "桌面设置未保存", exception.Message, ToolTipIcon.Warning);
        }
    }

    private static Image LoadEmbeddedImage(string resourceName)
    {
        using var stream = typeof(GatewayMainForm).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"找不到桌面资源：{resourceName}");
        using var source = Image.FromStream(stream);
        return new Bitmap(source);
    }

    private static string? GetBundledWebView2Runtime()
    {
        var runtimePath = Path.Combine(AppContext.BaseDirectory, "WebView2Runtime");
        return File.Exists(Path.Combine(runtimePath, "msedgewebview2.exe")) ? runtimePath : null;
    }

    private static void EnsureBundledRuntimePermissions(string runtimePath, string webViewData)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10) || OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return;
        }

        Directory.CreateDirectory(webViewData);
        var pathHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(runtimePath))))[..16];
        var markerPath = Path.Combine(webViewData, $"fixed-runtime-acl-{pathHash}.configured");
        if (File.Exists(markerPath))
        {
            return;
        }

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "icacls.exe"),
            UseShellExecute = false,
            CreateNoWindow = true
        };
        process.StartInfo.ArgumentList.Add(runtimePath);
        process.StartInfo.ArgumentList.Add("/grant");
        process.StartInfo.ArgumentList.Add("*S-1-15-2-2:(OI)(CI)(RX)");
        process.StartInfo.ArgumentList.Add("*S-1-15-2-1:(OI)(CI)(RX)");
        process.StartInfo.ArgumentList.Add("/T");
        process.StartInfo.ArgumentList.Add("/C");
        process.StartInfo.ArgumentList.Add("/Q");

        process.Start();
        if (!process.WaitForExit(30_000) || process.ExitCode != 0)
        {
            throw new InvalidOperationException("无法为内置 WebView2 设置 Windows 10 运行权限。请将程序解压到当前用户可写目录后重试。");
        }

        File.WriteAllText(markerPath, DateTimeOffset.Now.ToString("O"));
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (!_allowExit && eventArgs.CloseReason == CloseReason.UserClosing)
        {
            eventArgs.Cancel = true;
            HideToTray(showTip: true);
        }
    }

    protected override void OnHandleCreated(EventArgs eventArgs)
    {
        base.OnHandleCreated(eventArgs);
        CustomWindowChrome.ApplyVisuals(Handle);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmGetMinMaxInfo)
        {
            base.WndProc(ref message);
            CustomWindowChrome.ConstrainMaximizedBounds(Handle, message.LParam);
            return;
        }

        if (message.Msg == WmNcHitTest)
        {
            base.WndProc(ref message);
            var screenPoint = PointFromLParam(message.LParam);
            var clientPoint = PointToClient(screenPoint);

            if (WindowState == FormWindowState.Normal)
            {
                // The normal-state padding is an intentional client-area resize
                // strip. It stays HTCLIENT so the mouse overrides below receive
                // the events even when WebView2 occupies the rest of the form.
                if (GetResizeEdges(clientPoint) != ResizeEdges.None)
                {
                    message.Result = new IntPtr(HtClient);
                    return;
                }
            }

            if (_titleBar.RectangleToScreen(_titleBar.ClientRectangle).Contains(screenPoint)
                && !_minimizeButton.RectangleToScreen(_minimizeButton.ClientRectangle).Contains(screenPoint)
                && !_maximizeButton.RectangleToScreen(_maximizeButton.ClientRectangle).Contains(screenPoint)
                && !_closeButton.RectangleToScreen(_closeButton.ClientRectangle).Contains(screenPoint))
            {
                message.Result = new IntPtr(HtCaption);
                return;
            }

            message.Result = new IntPtr(HtClient);
            return;
        }

        base.WndProc(ref message);
    }

    protected override void OnMouseDown(MouseEventArgs eventArgs)
    {
        if (eventArgs.Button == MouseButtons.Left && WindowState == FormWindowState.Normal)
        {
            var edges = GetResizeEdges(eventArgs.Location);
            if (edges != ResizeEdges.None)
            {
                _activeResizeEdges = edges;
                _resizeStartPointer = MousePosition;
                _resizeStartBounds = Bounds;
                _manualResizing = true;
                Capture = true;
                return;
            }
        }

        base.OnMouseDown(eventArgs);
    }

    protected override void OnMouseMove(MouseEventArgs eventArgs)
    {
        if (_manualResizing)
        {
            ResizeFromPointer(MousePosition);
            return;
        }

        SetResizeCursor(GetResizeEdges(eventArgs.Location));
        base.OnMouseMove(eventArgs);
    }

    protected override void OnMouseUp(MouseEventArgs eventArgs)
    {
        if (eventArgs.Button == MouseButtons.Left && _manualResizing)
        {
            _manualResizing = false;
            _activeResizeEdges = ResizeEdges.None;
            Capture = false;
            Cursor = Cursors.Default;
            return;
        }

        base.OnMouseUp(eventArgs);
    }

    protected override void OnMouseLeave(EventArgs eventArgs)
    {
        if (!_manualResizing)
        {
            Cursor = Cursors.Default;
        }

        base.OnMouseLeave(eventArgs);
    }

    private ResizeEdges GetResizeEdges(Point clientPoint)
    {
        if (WindowState != FormWindowState.Normal)
        {
            return ResizeEdges.None;
        }

        var edges = ResizeEdges.None;
        if (clientPoint.X < ResizeBorder) edges |= ResizeEdges.Left;
        if (clientPoint.X >= ClientSize.Width - ResizeBorder) edges |= ResizeEdges.Right;
        if (clientPoint.Y < ResizeBorder) edges |= ResizeEdges.Top;
        if (clientPoint.Y >= ClientSize.Height - ResizeBorder) edges |= ResizeEdges.Bottom;
        return edges;
    }

    private void ResizeFromPointer(Point pointer)
    {
        var deltaX = pointer.X - _resizeStartPointer.X;
        var deltaY = pointer.Y - _resizeStartPointer.Y;
        var left = _resizeStartBounds.Left;
        var top = _resizeStartBounds.Top;
        var right = _resizeStartBounds.Right;
        var bottom = _resizeStartBounds.Bottom;

        if (_activeResizeEdges.HasFlag(ResizeEdges.Left)) left += deltaX;
        if (_activeResizeEdges.HasFlag(ResizeEdges.Right)) right += deltaX;
        if (_activeResizeEdges.HasFlag(ResizeEdges.Top)) top += deltaY;
        if (_activeResizeEdges.HasFlag(ResizeEdges.Bottom)) bottom += deltaY;

        if (right - left < MinimumSize.Width)
        {
            if (_activeResizeEdges.HasFlag(ResizeEdges.Left)) left = right - MinimumSize.Width;
            else right = left + MinimumSize.Width;
        }

        if (bottom - top < MinimumSize.Height)
        {
            if (_activeResizeEdges.HasFlag(ResizeEdges.Top)) top = bottom - MinimumSize.Height;
            else bottom = top + MinimumSize.Height;
        }

        Bounds = Rectangle.FromLTRB(left, top, right, bottom);
    }

    private void SetResizeCursor(ResizeEdges edges)
    {
        Cursor = edges switch
        {
            ResizeEdges.Left or ResizeEdges.Right => Cursors.SizeWE,
            ResizeEdges.Top or ResizeEdges.Bottom => Cursors.SizeNS,
            ResizeEdges.Left | ResizeEdges.Top or ResizeEdges.Right | ResizeEdges.Bottom => Cursors.SizeNWSE,
            ResizeEdges.Right | ResizeEdges.Top or ResizeEdges.Left | ResizeEdges.Bottom => Cursors.SizeNESW,
            _ => Cursors.Default
        };
    }

    [Flags]
    private enum ResizeEdges
    {
        None = 0,
        Left = 1,
        Top = 2,
        Right = 4,
        Bottom = 8
    }

    private void ToggleMaximized()
    {
        WindowState = WindowState == FormWindowState.Maximized
            ? FormWindowState.Normal
            : FormWindowState.Maximized;
    }

    private void UpdateWindowStateAppearance()
    {
        if (_maximizeButton is null)
        {
            return;
        }

        var maximized = WindowState == FormWindowState.Maximized;
        _maximizeButton.Text = maximized ? "\uE923" : "\uE922";
        Padding = maximized ? Padding.Empty : new Padding(ResizeBorder);
    }

    private static Point PointFromLParam(IntPtr lParam)
    {
        var value = lParam.ToInt64();
        return new Point(unchecked((short)(value & 0xffff)), unchecked((short)((value >> 16) & 0xffff)));
    }

    private void HideToTray(bool showTip)
    {
        Hide();
        if (showTip)
        {
            _trayIcon.ShowBalloonTip(1_500, "AiDataGateway 已转入后台", "安全网关仍在运行，双击托盘图标即可恢复。", ToolTipIcon.Info);
        }
    }

    private void RestoreWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _allowExit = true;
        _trayIcon.Visible = false;
        _memoryOverlay?.Close();
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trayIcon.Dispose();
            _trayMenu.Dispose();
            _memoryOverlay?.Dispose();
            _webView.Dispose();
            _brandImage.Dispose();
            _smallImage.Dispose();
            _appIcon.Dispose();
        }

        base.Dispose(disposing);
    }

    private sealed class GradientTitleBarPanel : Panel
    {
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Image? IconImage { get; init; }

        public GradientTitleBarPanel() => DoubleBuffered = true;

        protected override void OnPaintBackground(PaintEventArgs eventArgs)
        {
            if (ClientRectangle.Width <= 0 || ClientRectangle.Height <= 0)
            {
                return;
            }

            eventArgs.Graphics.SmoothingMode = SmoothingMode.HighQuality;
            using var gradient = new LinearGradientBrush(
                ClientRectangle,
                Color.FromArgb(15, 39, 69),
                Color.FromArgb(23, 105, 170),
                20F);
            eventArgs.Graphics.FillRectangle(gradient, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            base.OnPaint(eventArgs);
            if (IconImage is not null)
            {
                eventArgs.Graphics.DrawImage(IconImage, new Rectangle(12, 7, 30, 30));
            }

            using var titleFont = new Font("Segoe UI Semibold", 10.5F);
            using var subtitleFont = new Font("Microsoft YaHei UI", 8.5F);
            TextRenderer.DrawText(
                eventArgs.Graphics,
                "AiDataGateway",
                titleFont,
                new Rectangle(54, 0, 136, Height),
                Color.White,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(
                eventArgs.Graphics,
                "本地 AI 数据安全网关",
                subtitleFont,
                new Rectangle(198, 0, 210, Height),
                Color.FromArgb(200, 221, 241),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    private sealed class CaptionButton : Control
    {
        private readonly bool _closeButton;
        private bool _hovered;
        private bool _pressed;

        public CaptionButton(bool closeButton)
        {
            _closeButton = closeButton;
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.SupportsTransparentBackColor,
                true);
        }

        protected override void OnMouseEnter(EventArgs eventArgs)
        {
            _hovered = true;
            Invalidate();
            base.OnMouseEnter(eventArgs);
        }

        protected override void OnMouseLeave(EventArgs eventArgs)
        {
            _hovered = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(eventArgs);
        }

        protected override void OnMouseDown(MouseEventArgs eventArgs)
        {
            if (eventArgs.Button == MouseButtons.Left)
            {
                _pressed = true;
                Invalidate();
            }
            base.OnMouseDown(eventArgs);
        }

        protected override void OnMouseUp(MouseEventArgs eventArgs)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(eventArgs);
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            if (_hovered || _pressed)
            {
                var color = _closeButton
                    ? (_pressed ? Color.FromArgb(196, 14, 29) : Color.FromArgb(232, 17, 35))
                    : (_pressed ? Color.FromArgb(72, 8, 32, 58) : Color.FromArgb(44, 255, 255, 255));
                using var hoverBrush = new SolidBrush(color);
                eventArgs.Graphics.FillRectangle(hoverBrush, ClientRectangle);
            }

            TextRenderer.DrawText(
                eventArgs.Graphics,
                Text,
                Font,
                ClientRectangle,
                ForeColor,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix);
        }
    }

    private sealed class BrandCardPanel : Panel
    {
        public BrandCardPanel()
        {
            DoubleBuffered = true;
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var shadowPath = RoundedRectangle(new Rectangle(7, 9, Width - 14, Height - 14), 22);
            using var shadow = new SolidBrush(Color.FromArgb(28, 50, 77, 108));
            eventArgs.Graphics.FillPath(shadow, shadowPath);
            using var cardPath = RoundedRectangle(new Rectangle(3, 3, Width - 14, Height - 14), 22);
            using var card = new SolidBrush(Color.White);
            using var border = new Pen(Color.FromArgb(215, 226, 239));
            eventArgs.Graphics.FillPath(card, cardPath);
            eventArgs.Graphics.DrawPath(border, cardPath);
            base.OnPaint(eventArgs);
        }

        private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
        {
            var diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    private sealed class GatewayMenuColors : ProfessionalColorTable
    {
        public override Color MenuItemSelected => Color.FromArgb(224, 244, 250);
        public override Color MenuItemBorder => Color.FromArgb(21, 166, 205);
        public override Color ImageMarginGradientBegin => Color.White;
        public override Color ImageMarginGradientMiddle => Color.White;
        public override Color ImageMarginGradientEnd => Color.White;
        public override Color ToolStripDropDownBackground => Color.White;
        public override Color SeparatorDark => Color.FromArgb(218, 225, 235);
        public override Color SeparatorLight => Color.White;
    }
}
