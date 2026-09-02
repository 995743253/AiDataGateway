using System.IO;
using Microsoft.Win32;

namespace AiDataGateway.Desktop;

internal static class StorageMigrationService
{
    private static readonly HashSet<string> ExcludedTopLevelNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "WebView2",
        "Installer",
        "Updates"
    };

    public static string? Validate(string sourcePath, string targetPath, bool managedByEnvironment)
    {
        if (managedByEnvironment)
            return "当前数据库目录由环境变量 AI_GATEWAY_STORAGE_PATH 管理，请先移除该环境变量后再迁移。";
        if (string.IsNullOrWhiteSpace(targetPath)) return "请选择新的数据库目录。";

        string source;
        string target;
        try
        {
            source = Normalize(sourcePath);
            target = Normalize(targetPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return $"目标目录无效：{exception.Message}";
        }

        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase)) return "目标目录与当前数据库目录相同。";
        if (IsInside(target, source) || IsInside(source, target)) return "新旧数据库目录不能互相包含，请选择独立目录。";
        if (File.Exists(target)) return "目标路径是文件，请选择文件夹。";
        try
        {
            if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
                return "目标目录不是空目录，请新建或选择一个空目录，避免覆盖已有文件。";
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return $"无法访问目标目录：{exception.Message}";
        }

        return null;
    }

    public static void CopyAndSwitch(string sourcePath, string targetPath, DesktopHostConfiguration configuration)
    {
        var source = Normalize(sourcePath);
        var target = Normalize(targetPath);
        var targetParent = Path.GetDirectoryName(target) ?? throw new InvalidOperationException("无法确定目标目录的父目录。");
        Directory.CreateDirectory(targetParent);
        var staging = Path.Combine(targetParent, $".{Path.GetFileName(target)}.migrating-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            CopyDirectory(source, staging, isTopLevel: true);
            if (!File.Exists(Path.Combine(staging, "gateway.db")))
                throw new InvalidOperationException("迁移后的目录中未找到 gateway.db，未切换数据库路径。");

            if (Directory.Exists(target)) Directory.Delete(target, recursive: false);
            Directory.Move(staging, target);
            try
            {
                DesktopHostConfiguration.SaveStoragePath(AppContext.BaseDirectory, target, configuration.ListenAddress);
                TryUpdateInstallerDataPath(target);
            }
            catch
            {
                Directory.Delete(target, recursive: true);
                throw;
            }
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private static void CopyDirectory(string source, string target, bool isTopLevel)
    {
        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            var name = Path.GetFileName(directory);
            if (isTopLevel && ExcludedTopLevelNames.Contains(name)) continue;
            var destination = Path.Combine(target, name);
            Directory.CreateDirectory(destination);
            CopyDirectory(directory, destination, isTopLevel: false);
        }

        foreach (var file in Directory.EnumerateFiles(source))
        {
            var destination = Path.Combine(target, Path.GetFileName(file));
            File.Copy(file, destination, overwrite: false);
        }
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));

    private static bool IsInside(string candidate, string parent) =>
        candidate.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static void TryUpdateInstallerDataPath(string targetPath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\AiDataGateway", writable: true);
            key?.SetValue("DataPath", targetPath, RegistryValueKind.String);
        }
        catch
        {
            // The host configuration is authoritative. Registry metadata is best-effort only.
        }
    }
}
