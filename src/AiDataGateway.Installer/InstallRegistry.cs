using Microsoft.Win32;
using System.IO;

namespace AiDataGateway.Installer;

internal sealed record ExistingInstallation(string InstallPath, string DataPath, string Version);

internal static class InstallRegistry
{
    private const string ProductKey = @"Software\AiDataGateway";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\AiDataGateway";

    public static ExistingInstallation? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(ProductKey);
        var installPath = key?.GetValue("InstallPath") as string;
        var dataPath = key?.GetValue("DataPath") as string;
        if (string.IsNullOrWhiteSpace(installPath) || string.IsNullOrWhiteSpace(dataPath)) return null;
        return new ExistingInstallation(installPath, dataPath, key?.GetValue("Version") as string ?? "未知");
    }

    public static void Write(string installPath, string dataPath, string version, string uninstallerPath)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(ProductKey))
        {
            key.SetValue("InstallPath", installPath);
            key.SetValue("DataPath", dataPath);
            key.SetValue("Version", version);
        }

        using var uninstall = Registry.CurrentUser.CreateSubKey(UninstallKey);
        uninstall.SetValue("DisplayName", "AiDataGateway 本地 AI 数据安全网关");
        uninstall.SetValue("DisplayVersion", version);
        uninstall.SetValue("Publisher", "AiDataGateway");
        uninstall.SetValue("InstallLocation", installPath);
        uninstall.SetValue("DisplayIcon", Path.Combine(installPath, "AiDataGateway.Desktop.exe"));
        uninstall.SetValue("UninstallString", $"\"{uninstallerPath}\" --uninstall");
        uninstall.SetValue("NoModify", 1, RegistryValueKind.DWord);
        uninstall.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    public static void Remove()
    {
        Registry.CurrentUser.DeleteSubKeyTree(ProductKey, false);
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
    }
}
