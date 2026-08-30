using System.Text.Json;

namespace AiDataGateway.Desktop;

internal sealed record DesktopHostConfiguration(string StoragePath, string ListenAddress)
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

        return new DesktopHostConfiguration(Path.GetFullPath(storagePath), listenAddress.Trim());
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
