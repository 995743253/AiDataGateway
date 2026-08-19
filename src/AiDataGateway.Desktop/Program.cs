using AiDataGateway.Api;

namespace AiDataGateway.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            throw new InvalidOperationException("AiDataGateway desktop UI must start on an STA thread.");
        }

        using var singleInstance = new Mutex(initiallyOwned: true, "Local\\AiDataGateway.Desktop", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("AiDataGateway is already running.", "AiDataGateway", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        GatewayWebHost? webHost = null;
        try
        {
            var storagePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiDataGateway");
            webHost = GatewayWebHost.StartAsync(new GatewayHostOptions
            {
                Port = 5127,
                StoragePath = storagePath,
                WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot")
            }).GetAwaiter().GetResult();

            System.Windows.Forms.Application.Run(new GatewayMainForm(webHost.BaseAddress));
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.ToString(), "AiDataGateway failed to start", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (webHost is not null)
            {
                try
                {
                    webHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                catch (Exception exception)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to stop the local web host: {exception}");
                }
            }
        }
    }
}
