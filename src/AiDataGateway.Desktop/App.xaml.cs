using System.Threading;
using System.IO;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using AiDataGateway.Api;
using AiDataGateway.Application.DataSources;
using AiDataGateway.Domain.DataSources;
using Microsoft.Extensions.DependencyInjection;

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
        if (eventArgs.Args.Any(argument => string.Equals(argument, "--release-smoke-test", StringComparison.OrdinalIgnoreCase)))
        {
            Shutdown(RunReleaseSmokeTest());
            return;
        }

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

    private static int RunReleaseSmokeTest()
    {
        var storagePath = Path.Combine(Path.GetTempPath(), "AiDataGateway.ReleaseSmoke", Guid.NewGuid().ToString("N"));
        GatewayWebHost? webHost = null;
        MainWindow? window = null;
        try
        {
            Directory.CreateDirectory(storagePath);
            using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();

            webHost = GatewayWebHost.StartAsync(new GatewayHostOptions
            {
                Port = port,
                ListenAddress = "127.0.0.1",
                StoragePath = storagePath,
                WebRootPath = Path.Combine(storagePath, "wwwroot"),
                UseEphemeralCertificates = true
            }).GetAwaiter().GetResult();

            using var client = new HttpClient { BaseAddress = webHost.BaseAddress };
            using var response = client.GetAsync("/api/health").GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();

            // Exercise the data source save pipeline against the protected assemblies:
            // Domain's blocked-table normalization runs through compiler-generated lambda
            // classes (<>c) that string obfuscation previously corrupted — type load
            // failed at runtime while the health check still passed.
            using (var scope = webHost.Services.CreateScope())
            {
                var dataSourceService = scope.ServiceProvider.GetRequiredService<DataSourceService>();
                var createdDataSource = dataSourceService.CreateAsync(new DataSourceUpsertRequest(
                    "smoke-datasource", "Release Smoke", DatabaseProvider.Sqlite, "127.0.0.1", 1,
                    Path.Combine(storagePath, "smoke.db"), "smoke", "smoke",
                    DataSourceAccessMode.ReadOnly, BlockedTables: new[] { "smoke_secret", " dbo.users " }),
                    "release-smoke").GetAwaiter().GetResult();
                if (createdDataSource.BlockedTables.Count != 2 ||
                    createdDataSource.BlockedTables[0] != "smoke_secret" ||
                    createdDataSource.BlockedTables[1] != "dbo.users")
                {
                    throw new InvalidDataException("Blocked tables were not normalized and preserved on save.");
                }
            }

            // Constructing the window validates that compiled XAML/BAML bindings load correctly.
            window = new MainWindow(webHost.BaseAddress, storagePath, false, _ => Task.FromResult<string?>(null));
            window.DisposeForReleaseSmokeTest();
            window = null;
            return 0;
        }
        catch (Exception exception)
        {
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "release-smoke-error.txt"), exception.ToString()); } catch { }
            return 1;
        }
        finally
        {
            window?.DisposeForReleaseSmokeTest();
            if (webHost is not null)
            {
                try { Task.Run(() => webHost.DisposeAsync().AsTask()).Wait(TimeSpan.FromSeconds(5)); } catch { }
            }
            try { Directory.Delete(storagePath, true); } catch { }
        }
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
