using System.Text.Json;
using System.IO;

namespace AiDataGateway.Desktop;

internal sealed record DesktopHostConfiguration(string StoragePath, string ListenAddress, bool StoragePathManagedByEnvironment)
{
    private const string ConfigurationFileName = "gateway.host.json";

    public static DesktopHostConfiguration Load(string applicationDirectory)
    {
        var baseDirectory = Path.GetFullPath(applicationDirectory);
        var fileSettings = LoadFileSettings(Path.Combine(baseDirectory, ConfigurationFileName));

        var configuredStoragePath = FirstNotEmpty(
            Environment.GetEnvironmentVariable("AI_GATEWAY_STORAGE_PATH"),
            fileSettings?.StoragePath);

        var storagePath = string.IsNullOrWhiteSpace(configuredStoragePath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiDataGateway")
            : ResolvePath(configuredStoragePath, baseDirectory);

        var listenAddress = FirstNotEmpty(
            Environment.GetEnvironmentVariable("AI_GATEWAY_LISTEN_ADDRESS"),
            fileSettings?.ListenAddress,
            "127.0.0.1")!;

        return new DesktopHostConfiguration(
            Path.GetFullPath(storagePath),
            listenAddress.Trim(),
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AI_GATEWAY_STORAGE_PATH")));
    }

    public static void SaveStoragePath(string applicationDirectory, string storagePath, string listenAddress)
    {
        var configurationPath = Path.Combine(Path.GetFullPath(applicationDirectory), ConfigurationFileName);
        var temporaryPath = configurationPath + ".tmp";
        var json = JsonSerializer.Serialize(new GatewayHostFileSettings
        {
            StoragePath = Path.GetFullPath(storagePath),
            ListenAddress = listenAddress
        }, new JsonSerializerOptions { WriteIndented = true });

        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, configurationPath, overwrite: true);
    }

    private static GatewayHostFileSettings? LoadFileSettings(string path)
    {
        if (!File.Exists(path)) return null;

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<GatewayHostFileSettings>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });
    }

    private static string ResolvePath(string configuredPath, string baseDirectory)
    {
        var expanded = Environment.ExpandEnvironmentVariables(configuredPath.Trim());
        return Path.IsPathFullyQualified(expanded)
            ? expanded
            : Path.Combine(baseDirectory, expanded);
    }

    private static string? FirstNotEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private sealed class GatewayHostFileSettings
    {
        public string? StoragePath { get; init; }
        public string? ListenAddress { get; init; }
    }
}
