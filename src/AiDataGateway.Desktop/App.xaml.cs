using System.Threading;
using System.IO;
using System.Windows;
using AiDataGateway.Api;

namespace AiDataGateway.Desktop;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private GatewayWebHost? _webHost;

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
        var window = new MainWindow(webHost.BaseAddress, hostConfiguration.StoragePath);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs eventArgs)
    {
        if (_webHost is not null)
        {
            try
            {
                _webHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
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
