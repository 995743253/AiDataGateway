using Microsoft.Web.WebView2.WinForms;

namespace AiDataGateway.Desktop;

internal sealed class GatewayMainForm : Form
{
    private readonly Uri _baseAddress;
    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };
    private readonly ToolStripStatusLabel _status = new() { Text = "Starting..." };
    private readonly NotifyIcon _trayIcon;
    private bool _allowExit;

    public GatewayMainForm(Uri baseAddress)
    {
        _baseAddress = baseAddress;
        Text = "AiDataGateway - 本地 AI 数据访问管控";
        Width = 1280;
        Height = 820;
        MinimumSize = new Size(960, 640);
        StartPosition = FormStartPosition.CenterScreen;

        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(_status);
        Controls.Add(_webView);
        Controls.Add(statusStrip);

        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("打开", null, (_, _) => RestoreWindow());
        trayMenu.Items.Add("退出", null, (_, _) => ExitApplication());
        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Shield,
            Text = "AiDataGateway",
            Visible = true,
            ContextMenuStrip = trayMenu
        };
        _trayIcon.DoubleClick += (_, _) => RestoreWindow();

        Shown += async (_, _) => await InitializeWebViewAsync();
        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized)
            {
                Hide();
            }
        };
        FormClosing += OnFormClosing;
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            var webViewData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiDataGateway", "WebView2");
            var bundledRuntime = GetBundledWebView2Runtime();
            if (bundledRuntime is not null)
            {
                EnsureBundledRuntimePermissions(bundledRuntime, webViewData);
            }

            var environment = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: bundledRuntime,
                userDataFolder: webViewData);
            await _webView.EnsureCoreWebView2Async(environment);
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            _webView.Source = _baseAddress;
            _status.Text = bundledRuntime is null
                ? $"运行中 · {_baseAddress} · 系统 WebView2"
                : $"运行中 · {_baseAddress} · 内置 WebView2";
        }
        catch (Exception exception)
        {
            _status.Text = "WebView2 初始化失败";
            MessageBox.Show(exception.Message, "WebView2 error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
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

        using var process = new System.Diagnostics.Process();
        process.StartInfo = new System.Diagnostics.ProcessStartInfo
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
            Hide();
            _trayIcon.ShowBalloonTip(1_500, "AiDataGateway", "程序仍在后台运行。", ToolTipIcon.Info);
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
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trayIcon.Dispose();
            _webView.Dispose();
        }

        base.Dispose(disposing);
    }
}
