using Microsoft.Win32;
using System.IO;
using System.Windows;

namespace AiDataGateway.Installer;

public partial class MainWindow : Window
{
    private readonly InstallerArguments _arguments;
    private readonly ExistingInstallation? _existing;
    private bool _working;

    internal MainWindow(InstallerArguments arguments, ExistingInstallation? existing)
    {
        InitializeComponent();
        _arguments = arguments;
        _existing = existing;
        InstallPathBox.Text = existing?.InstallPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "AiDataGateway");
        DataPathBox.Text = existing?.DataPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiDataGateway");

        if (arguments.Uninstall)
        {
            ModeTitle.Text = "卸载 AiDataGateway";
            ModeDescription.Text = "程序文件和快捷方式将被删除，数据库、密钥与日志会保留。";
            InstallButton.Content = "卸载";
            InstallPathBox.IsReadOnly = DataPathBox.IsReadOnly = true;
            BrowseDataButton.IsEnabled = false;
            DesktopShortcutCheck.Visibility = Visibility.Collapsed;
        }
        else if (existing is not null)
        {
            ModeTitle.Text = $"升级 AiDataGateway（当前 {existing.Version}）";
            ModeDescription.Text = "已检测到安装目录。升级只替换程序文件，并沿用现有数据库目录。";
            InstallButton.Content = "更新";
            InstallPathBox.IsReadOnly = DataPathBox.IsReadOnly = true;
            BrowseDataButton.IsEnabled = false;
        }
        HeaderText.Text = $"版本 {InstallerEngine.Version} · 安装与更新程序";
    }

    private void OnBrowseInstall(object sender, RoutedEventArgs e)
    {
        if (_existing is not null || _arguments.Uninstall) return;
        var dialog = new OpenFolderDialog { Title = "选择程序安装目录", InitialDirectory = InstallPathBox.Text, Multiselect = false };
        if (dialog.ShowDialog(this) == true) InstallPathBox.Text = dialog.FolderName;
    }

    private void OnBrowseData(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择数据库与密钥目录", InitialDirectory = DataPathBox.Text, Multiselect = false };
        if (dialog.ShowDialog(this) == true) DataPathBox.Text = dialog.FolderName;
    }

    private async void OnInstall(object sender, RoutedEventArgs e)
    {
        if (_working) return;
        if (_arguments.Uninstall)
        {
            if (MessageBox.Show($"确定卸载 AiDataGateway？\n\n业务数据将保留在：\n{DataPathBox.Text}", "确认卸载", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        }
        _working = true;
        InstallButton.IsEnabled = CancelButton.IsEnabled = false;
        InstallProgress.Visibility = Visibility.Visible;
        try
        {
            if (_arguments.Uninstall)
            {
                StatusText.Text = "正在删除程序文件…";
                await InstallerEngine.UninstallAsync(_existing, _arguments.WaitPid, CancellationToken.None);
                MessageBox.Show($"卸载完成。业务数据仍保留在：\n{DataPathBox.Text}", "AiDataGateway", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                StatusText.Text = _existing is null ? "正在安装…" : "正在更新…";
                var progress = new Progress<int>(value => InstallProgress.Value = value);
                var request = new InstallerRequest(InstallPathBox.Text, DataPathBox.Text, _arguments.WaitPid, DesktopShortcutCheck.IsChecked == true);
                await InstallerEngine.InstallAsync(request, progress, CancellationToken.None);
                StatusText.Text = "完成";
                if (MessageBox.Show("安装完成，是否立即启动 AiDataGateway？", "AiDataGateway", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                    InstallerEngine.Launch(request.InstallPath);
            }
            Close();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "操作失败", MessageBoxButton.OK, MessageBoxImage.Error);
            InstallButton.IsEnabled = CancelButton.IsEnabled = true;
            _working = false;
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) { if (!_working) Close(); }
}
