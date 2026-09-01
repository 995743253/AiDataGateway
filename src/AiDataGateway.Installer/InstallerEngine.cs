using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace AiDataGateway.Installer;

internal sealed record InstallerRequest(string InstallPath, string DataPath, int? WaitPid, bool CreateDesktopShortcut)
{
    public static InstallerRequest ForSilent(InstallerArguments arguments, ExistingInstallation? existing)
    {
        var installPath = existing?.InstallPath ?? arguments.InstallPath;
        var dataPath = existing?.DataPath ?? arguments.DataPath;
        if (string.IsNullOrWhiteSpace(installPath) || string.IsNullOrWhiteSpace(dataPath))
        {
            throw new InvalidOperationException("静默安装缺少安装目录或数据目录。");
        }
        return new InstallerRequest(installPath, dataPath, arguments.WaitPid, true);
    }
}

internal static class InstallerEngine
{
    private const string PayloadResource = "AiDataGateway.Installer.Payload.zip";
    private const string ProductExe = "AiDataGateway.Desktop.exe";

    public static string Version => (Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];

    public static void VerifyPayload()
    {
        using var payload = OpenPayload();
        using var archive = new ZipArchive(payload, ZipArchiveMode.Read);
        var required = new[] { ProductExe, "wwwroot/index.html", "gateway.host.example.json" };
        foreach (var name in required)
        {
            if (!archive.Entries.Any(entry => entry.FullName.Replace('\\', '/').Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException($"安装载荷缺少必要文件：{name}");
        }
    }

    public static async Task InstallAsync(InstallerRequest request, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        var installPath = ValidateDirectory(request.InstallPath, "安装目录");
        var dataPath = ValidateDirectory(request.DataPath, "数据库目录");
        if (Path.GetPathRoot(installPath)?.Equals(installPath, StringComparison.OrdinalIgnoreCase) == true)
            throw new InvalidOperationException("安装目录不能是磁盘根目录。");
        if (string.Equals(installPath, dataPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("数据库目录不能与程序目录完全相同；可以选择程序目录下的 data 子目录。");

        await WaitForProcessAsync(request.WaitPid, cancellationToken);
        EnsureGatewayStopped();
        progress?.Report(5);

        var staging = Path.Combine(Path.GetTempPath(), $"AiDataGateway-install-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            await using var payload = OpenPayload();
            using (var archive = new ZipArchive(payload, ZipArchiveMode.Read, leaveOpen: false))
            {
                var total = Math.Max(archive.Entries.Count, 1);
                var index = 0;
                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var destination = Path.GetFullPath(Path.Combine(staging, entry.FullName));
                    if (!destination.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("安装包包含非法路径。");
                    if (string.IsNullOrEmpty(entry.Name)) Directory.CreateDirectory(destination);
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        await using var source = entry.Open();
                        await using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, true);
                        await source.CopyToAsync(target, cancellationToken);
                    }
                    progress?.Report(5 + (++index * 55 / total));
                }
            }

            Directory.CreateDirectory(installPath);
            Directory.CreateDirectory(dataPath);
            var files = Directory.GetFiles(staging, "*", SearchOption.AllDirectories);
            for (var index = 0; index < files.Length; index++)
            {
                var relative = Path.GetRelativePath(staging, files[index]);
                var destination = Path.Combine(installPath, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(files[index], destination, true);
                progress?.Report(60 + ((index + 1) * 28 / Math.Max(files.Length, 1)));
            }

            var configurationPath = Path.Combine(installPath, "gateway.host.json");
            if (!File.Exists(configurationPath))
            {
                await File.WriteAllTextAsync(configurationPath, JsonSerializer.Serialize(new
                {
                    storagePath = dataPath,
                    listenAddress = "127.0.0.1"
                }, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
            }

            var uninstallerDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiDataGateway", "Installer");
            Directory.CreateDirectory(uninstallerDirectory);
            var uninstallerPath = Path.Combine(uninstallerDirectory, "AiDataGateway-Uninstall.exe");
            var currentProcess = Environment.ProcessPath ?? throw new InvalidOperationException("无法定位安装器文件。");
            if (!string.Equals(currentProcess, uninstallerPath, StringComparison.OrdinalIgnoreCase)) File.Copy(currentProcess, uninstallerPath, true);

            CreateShortcut(StartMenuShortcutPath(), Path.Combine(installPath, ProductExe), installPath);
            if (request.CreateDesktopShortcut) CreateShortcut(DesktopShortcutPath(), Path.Combine(installPath, ProductExe), installPath);
            InstallRegistry.Write(installPath, dataPath, Version, uninstallerPath);
            progress?.Report(100);
        }
        finally
        {
            try { Directory.Delete(staging, true); } catch { }
        }
    }

    public static async Task UninstallAsync(ExistingInstallation? installation, int? waitPid, CancellationToken cancellationToken)
    {
        if (installation is null) return;
        await WaitForProcessAsync(waitPid, cancellationToken);
        EnsureGatewayStopped();
        DeleteProgramFilesPreservingData(installation.InstallPath, installation.DataPath);
        TryDelete(StartMenuShortcutPath());
        TryDelete(DesktopShortcutPath());
        InstallRegistry.Remove();
    }

    public static void Launch(string installPath) => Process.Start(new ProcessStartInfo(Path.Combine(installPath, ProductExe))
    {
        UseShellExecute = true,
        WorkingDirectory = installPath
    });

    private static Stream OpenPayload()
    {
        var embedded = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResource);
        if (embedded is not null) return embedded;
        var adjacent = Path.Combine(AppContext.BaseDirectory, "AiDataGateway-payload.zip");
        if (File.Exists(adjacent)) return File.OpenRead(adjacent);
        throw new FileNotFoundException("安装器没有包含应用程序载荷，请使用发布页中的完整 Setup.exe。", adjacent);
    }

    private static string ValidateDirectory(string path, string label)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException($"请选择{label}。");
        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim()));
    }

    private static async Task WaitForProcessAsync(int? processId, CancellationToken cancellationToken)
    {
        if (!processId.HasValue) return;
        try
        {
            using var process = Process.GetProcessById(processId.Value);
            await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(45), cancellationToken);
        }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }
    }

    private static void EnsureGatewayStopped()
    {
        var running = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ProductExe))
            .Any(process => process.Id != Environment.ProcessId);
        if (running) throw new InvalidOperationException("AiDataGateway 仍在运行。请从托盘菜单退出程序后重试。");
    }

    private static void DeleteProgramFilesPreservingData(string installPath, string dataPath)
    {
        if (!Directory.Exists(installPath)) return;
        var install = Path.GetFullPath(installPath).TrimEnd(Path.DirectorySeparatorChar);
        var data = Path.GetFullPath(dataPath).TrimEnd(Path.DirectorySeparatorChar);
        if (!data.StartsWith(install + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            Directory.Delete(install, true);
            return;
        }

        foreach (var file in Directory.GetFiles(install)) TryDelete(file);
        foreach (var directory in Directory.GetDirectories(install))
        {
            var full = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
            if (data.Equals(full, StringComparison.OrdinalIgnoreCase) || data.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            try { Directory.Delete(full, true); } catch { }
        }
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("无法创建 Windows 快捷方式。");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = targetPath;
        shortcut.WorkingDirectory = workingDirectory;
        shortcut.IconLocation = targetPath;
        shortcut.Description = "AiDataGateway 本地 AI 数据安全网关";
        shortcut.Save();
        Marshal.FinalReleaseComObject(shortcut);
        Marshal.FinalReleaseComObject(shell);
    }

    private static string StartMenuShortcutPath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "AiDataGateway.lnk");
    private static string DesktopShortcutPath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "AiDataGateway.lnk");
    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
}
