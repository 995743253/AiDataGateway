using System.Threading;
using System.IO;
using System.Diagnostics;
using System.Windows;
using AiDataGateway.Api;

namespace AiDataGateway.Desktop;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private GatewayWebHost? _webHost;
    private DesktopHostConfiguration? _hostConfiguration;
    private bool _migrationInProgress;

    protected override void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);
        _singleInstance = new Mutex(initiallyOwned: true, @"Local\AiDataGateway.Desktop", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("AiDataGateway is already running.", "AiDataGateway", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(0);
            return;
        }

        GatewayWebHost webHost;
        DesktopHostConfiguration hostConfiguration;
        try
        {
            hostConfiguration = DesktopHostConfiguration.Load(AppContext.BaseDirectory);
            webHost = GatewayWebHost.StartAsync(new GatewayHostOptions
            {
                Port = 5127,
                ListenAddress = hostConfiguration.ListenAddress,
                StoragePath = hostConfiguration.StoragePath,
                WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot")
            }).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.ToString(), "AiDataGateway failed to start", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        _webHost = webHost;
        _hostConfiguration = hostConfiguration;
        var window = new MainWindow(
            webHost.BaseAddress,
            hostConfiguration.StoragePath,
            hostConfiguration.StoragePathManagedByEnvironment,
            MigrateStorageAsync);
        MainWindow = window;
        window.Show();
    }

    private async Task<string?> MigrateStorageAsync(string targetPath)
    {
        if (_migrationInProgress) return "数据库迁移正在进行，请勿重复提交。";
        if (_hostConfiguration is null || _webHost is null) return "本地服务尚未准备好。";

        var validationError = StorageMigrationService.Validate(
            _hostConfiguration.StoragePath,
            targetPath,
            _hostConfiguration.StoragePathManagedByEnvironment);
        if (validationError is not null) return validationError;

        _migrationInProgress = true;
        try
        {
            // SQLite and the DPAPI key ring must be closed before a consistent copy is made.
            await _webHost.DisposeAsync();
            _webHost = null;
            StorageMigrationService.CopyAndSwitch(_hostConfiguration.StoragePath, targetPath, _hostConfiguration);
            RestartApplication();
            return null;
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"数据库迁移失败，原目录没有删除。程序将使用原配置重新启动。\n\n{exception.Message}",
                "数据库迁移失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            RestartApplication();
            return exception.Message;
        }
    }

    private void RestartApplication()
    {
        var executablePath = Environment.ProcessPath;
        try
        {
            _singleInstance?.ReleaseMutex();
            _singleInstance?.Dispose();
            _singleInstance = null;
        }
        catch (ApplicationException)
        {
            // The mutex may already have been released while the process is exiting.
        }
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = true });
        }
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs eventArgs)
    {
        if (_webHost is not null)
        {
            // The console page keeps an SSE connection open, which StopAsync
            // would otherwise wait on for the full graceful-shutdown timeout
            // while the UI thread is blocked here. Bound the wait instead.
            try
            {
                Task.Run(() => _webHost.DisposeAsync().AsTask()).Wait(TimeSpan.FromSeconds(3));
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to stop the local web host: {exception}");
            }
        }

        _singleInstance?.Dispose();
        base.OnExit(eventArgs);
    }
}
