using System.Windows;

namespace AiDataGateway.Installer;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);
        var arguments = InstallerArguments.Parse(eventArgs.Args);
        var installation = InstallRegistry.Read();

        if (arguments.Verify)
        {
            try
            {
                InstallerEngine.VerifyPayload();
                Shutdown(0);
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "AiDataGateway 安装包校验失败", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
            return;
        }

        if (arguments.Silent)
        {
            try
            {
                if (arguments.Uninstall)
                {
                    await InstallerEngine.UninstallAsync(installation, arguments.WaitPid, CancellationToken.None);
                }
                else
                {
                    var request = InstallerRequest.ForSilent(arguments, installation);
                    await InstallerEngine.InstallAsync(request, null, CancellationToken.None);
                    if (arguments.Launch) InstallerEngine.Launch(request.InstallPath);
                }
                Shutdown(0);
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "AiDataGateway 安装失败", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
            return;
        }

        var window = new MainWindow(arguments, installation);
        MainWindow = window;
        window.Show();
    }
}
