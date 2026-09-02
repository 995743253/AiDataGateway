using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Web.WebView2.Core;

namespace AiDataGateway.Desktop;

public partial class MainWindow : Window
{
    private static readonly Color Green = Color.FromRgb(0x1F, 0xB2, 0x78);
    private static readonly Color Amber = Color.FromRgb(0xF2, 0xA8, 0x2D);
    private static readonly Color Red = Color.FromRgb(0xE1, 0x4A, 0x55);

    private readonly Uri _baseAddress;
    private readonly string _storagePath;
    private readonly bool _storagePathManagedByEnvironment;
    private readonly Func<string, Task<string?>> _migrateStorageAsync;
    private readonly DesktopSettingsStore _desktopSettingsStore;
    private readonly GitHubUpdateService _updateService = new();
    private DesktopSettings _desktopSettings;
    private TaskbarIcon _trayIcon = null!;
    private MenuItem _memoryOverlayMenuItem = null!;
    private MemoryOverlayWindow? _memoryOverlay;
    private bool _allowExit;
    private bool _checkingForUpdate;

    public MainWindow(
        Uri baseAddress,
        string storagePath,
        bool storagePathManagedByEnvironment,
        Func<string, Task<string?>> migrateStorageAsync)
    {
        InitializeComponent();
        _baseAddress = baseAddress;
        _storagePath = storagePath;
        _storagePathManagedByEnvironment = storagePathManagedByEnvironment;
        _migrateStorageAsync = migrateStorageAsync;
        _desktopSettingsStore = new DesktopSettingsStore(storagePath);
        _desktopSettings = _desktopSettingsStore.Load();

        EndpointText.Text = $"本地端点  {_baseAddress}";
        BuildTrayIcon();

        SourceInitialized += (_, _) =>
        {
            var handle = CustomWindowChrome.HookMinMaxInfo(this);
            CustomWindowChrome.ApplyVisuals(handle);
        };
        StateChanged += (_, _) =>
        {
            UpdateMaximizeGlyph();
            if (WindowState == WindowState.Minimized)
            {
                HideToTray(showTip: false);
            }
        };
        Closing += OnWindowClosing;
        PreviewKeyDown += (_, eventArgs) =>
        {
            if (eventArgs.Key == System.Windows.Input.Key.F5)
            {
                ReloadWebView();
                eventArgs.Handled = true;
            }
            else if ((eventArgs.KeyboardDevice.Modifiers & (System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift))
                     == (System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift)
                     && eventArgs.Key == System.Windows.Input.Key.O)
            {
                OpenInBrowser();
                eventArgs.Handled = true;
            }
        };
        Loaded += async (_, _) =>
        {
            ApplyMemoryOverlaySetting();
            await InitializeWebViewAsync();
            await Task.Delay(1500);
            await CheckForUpdatesAsync(silentWhenCurrent: true);
        };
    }

    private void BuildTrayIcon()
    {
        var menu = new ContextMenu();
        var openItem = new MenuItem { Header = "打开控制台", FontWeight = FontWeights.SemiBold };
        openItem.Click += (_, _) => RestoreWindow();
        var reloadItem = new MenuItem { Header = "刷新页面" };
        reloadItem.Click += (_, _) => ReloadWebView();
        var browserItem = new MenuItem { Header = "使用浏览器打开" };
        browserItem.Click += (_, _) => OpenInBrowser();
        var updateItem = new MenuItem { Header = "检查更新" };
        updateItem.Click += async (_, _) => await CheckForUpdatesAsync(silentWhenCurrent: false);
        _memoryOverlayMenuItem = new MenuItem
        {
            Header = "显示内存悬浮球",
            IsCheckable = true,
            IsChecked = _desktopSettings.MemoryOverlayEnabled
        };
        _memoryOverlayMenuItem.Click += (_, _) => SetMemoryOverlayEnabled(_memoryOverlayMenuItem.IsChecked);
        var exitItem = new MenuItem { Header = "退出 AiDataGateway" };
        exitItem.Click += (_, _) => ExitApplication();

        menu.Items.Add(openItem);
        menu.Items.Add(reloadItem);
        menu.Items.Add(browserItem);
        menu.Items.Add(updateItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(_memoryOverlayMenuItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "AiDataGateway · 本地 AI 数据安全网关",
            IconSource = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/gateway-app.ico")),
            ContextMenu = menu
        };
        _trayIcon.TrayMouseDoubleClick += (_, _) => RestoreWindow();
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
            await WebView.EnsureCoreWebView2Async(environment);
            WebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            WebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            WebView.CoreWebView2.NavigationStarting += (_, _) => SetStatus("正在加载管理控制台…", Amber);
            WebView.CoreWebView2.NavigationCompleted += (_, eventArgs) =>
            {
                if (eventArgs.IsSuccess)
                {
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                    WebView.Focus();
                    SetStatus("安全网关运行中", Green);
                    SendDesktopState();
                }
                else
                {
                    LoadingTitle.Text = "控制台加载失败";
                    LoadingMessage.Text = $"WebView2 错误：{eventArgs.WebErrorStatus}";
                    SetStatus("控制台加载失败", Red);
                }
            };
            EndpointText.Text = bundledRuntime is null
                ? $"{_baseAddress}  ·  系统 WebView2"
                : $"{_baseAddress}  ·  内置 WebView2";
            WebView.Source = _baseAddress;
        }
        catch (Exception exception)
        {
            LoadingTitle.Text = "WebView2 初始化失败";
            LoadingMessage.Text = "请检查运行环境后重试，或使用右下角按钮在浏览器中打开。";
            SetStatus("WebView2 初始化失败", Red);
            MessageBox.Show(exception.Message, "AiDataGateway · 初始化失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetStatus(string text, Color color)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => SetStatus(text, color));
            return;
        }

        StatusText.Text = text;
        StatusDot.Fill = new SolidColorBrush(color);
    }

    private void ReloadWebView()
    {
        if (WebView.CoreWebView2 is null)
        {
            return;
        }

        WebView.CoreWebView2.Reload();
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
            MessageBox.Show(exception.Message, "无法打开浏览器", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task CheckForUpdatesAsync(bool silentWhenCurrent)
    {
        if (_checkingForUpdate) return;
        _checkingForUpdate = true;
        try
        {
            SetStatus("正在检查更新…", Amber);
            var update = await _updateService.CheckAsync();
            if (update is null)
            {
                SetStatus("安全网关运行中", Green);
                if (!silentWhenCurrent)
                    MessageBox.Show($"当前版本 {GitHubUpdateService.CurrentVersionText} 已是最新版。", "检查更新", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"发现新版本 {update.VersionText}（当前 {GitHubUpdateService.CurrentVersionText}）。\n\n是否下载并自动更新？程序目录和数据库目录都会保持不变。",
                "AiDataGateway 更新", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (result != MessageBoxResult.Yes)
            {
                SetStatus($"发现新版本 {update.VersionText}", Amber);
                return;
            }

            var progress = new Progress<int>(value => SetStatus($"正在下载更新… {value}%", Amber));
            var installer = await _updateService.DownloadAsync(update, progress);
            SetStatus("正在启动更新程序…", Amber);
            GitHubUpdateService.StartInstaller(installer);
            ExitApplication();
        }
        catch (Exception exception)
        {
            SetStatus(silentWhenCurrent ? "安全网关运行中" : "检查更新失败", silentWhenCurrent ? Green : Red);
            if (!silentWhenCurrent)
                MessageBox.Show(exception.Message, "检查更新失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _checkingForUpdate = false;
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
                case "desktop.storage.choose":
                    ChooseStorageDirectory();
                    break;
                case "desktop.storage.migrate" when message.RootElement.TryGetProperty("targetPath", out var targetPathProperty):
                    _ = RequestStorageMigrationAsync(targetPathProperty.GetString());
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
        WebView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            type = "desktop.state",
            available = true,
            memoryOverlayEnabled = _desktopSettings.MemoryOverlayEnabled,
            storagePath = _storagePath,
            storageMigrationAvailable = !_storagePathManagedByEnvironment,
            storagePathManagedByEnvironment = _storagePathManagedByEnvironment
        }));
    }

    private void ChooseStorageDirectory()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择新的 AiDataGateway 数据库目录",
            InitialDirectory = Directory.Exists(_storagePath) ? Path.GetDirectoryName(_storagePath) : null,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;
        PostDesktopMessage(new { type = "desktop.storage.selection", path = dialog.FolderName });
    }

    private async Task RequestStorageMigrationAsync(string? targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            PostDesktopMessage(new { type = "desktop.storage.migrationResult", success = false, message = "请选择新的数据库目录。" });
            return;
        }

        SetStatus("正在迁移数据库并准备重启…", Amber);
        var error = await _migrateStorageAsync(targetPath);
        if (error is null) return;
        SetStatus("数据库迁移未执行", Red);
        PostDesktopMessage(new { type = "desktop.storage.migrationResult", success = false, message = error });
    }

    private void PostDesktopMessage(object message) =>
        WebView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(message));

    private void SetMemoryOverlayEnabled(bool enabled)
    {
        _desktopSettings = _desktopSettings with { MemoryOverlayEnabled = enabled };
        SaveDesktopSettings();
        ApplyMemoryOverlaySetting();
        SendDesktopState();
    }

    private void ApplyMemoryOverlaySetting()
    {
        _memoryOverlayMenuItem.IsChecked = _desktopSettings.MemoryOverlayEnabled;
        if (!_desktopSettings.MemoryOverlayEnabled)
        {
            if (_memoryOverlay is not null)
            {
                _memoryOverlay.Close();
                _memoryOverlay = null;
            }
            return;
        }

        if (_memoryOverlay is { IsLoaded: true })
        {
            return;
        }

        _memoryOverlay = new MemoryOverlayWindow
        {
            Left = _desktopSettings.MemoryOverlayX ?? -1,
            Top = _desktopSettings.MemoryOverlayY ?? -1
        };
        _memoryOverlay.PositionCommitted += point =>
        {
            _desktopSettings = _desktopSettings with { MemoryOverlayX = (int)point.X, MemoryOverlayY = (int)point.Y };
            SaveDesktopSettings();
        };
        _memoryOverlay.OpenConsoleRequested += RestoreWindow;
        _memoryOverlay.DisableRequested += () => SetMemoryOverlayEnabled(false);
        _memoryOverlay.Show();
    }

    private void SaveDesktopSettings()
    {
        try
        {
            _desktopSettingsStore.Save(_desktopSettings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _trayIcon.ShowBalloonTip("桌面设置未保存", exception.Message, BalloonIcon.Warning);
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

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs eventArgs)
    {
        if (_allowExit) return;
        eventArgs.Cancel = true;
        HideToTray(showTip: true);
    }

    private void UpdateMaximizeGlyph()
    {
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }

    private void HideToTray(bool showTip)
    {
        Hide();
        if (showTip)
        {
            _trayIcon.ShowBalloonTip("AiDataGateway 已转入后台", "安全网关仍在运行，双击托盘图标即可恢复。", BalloonIcon.Info);
        }
    }

    private void RestoreWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _allowExit = true;
        // The tray menu runs its own message pump; dismissing it and deferring
        // Shutdown by one dispatcher cycle keeps its popup hwnd from freezing
        // on screen while the application tears down.
        if (_trayIcon.ContextMenu is { } menu) menu.IsOpen = false;
        _trayIcon.Dispose();
        _memoryOverlay?.Close();
        Close();
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => System.Windows.Application.Current.Shutdown()));
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs eventArgs) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs eventArgs) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object sender, RoutedEventArgs eventArgs) => Close();

    private void OnReloadClick(object sender, RoutedEventArgs eventArgs) => ReloadWebView();

    private void OnOpenInBrowserClick(object sender, RoutedEventArgs eventArgs) => OpenInBrowser();

    private void OnHideToTrayClick(object sender, RoutedEventArgs eventArgs) => HideToTray(showTip: true);

    private async void OnCheckUpdateClick(object sender, RoutedEventArgs eventArgs) => await CheckForUpdatesAsync(silentWhenCurrent: false);
}
