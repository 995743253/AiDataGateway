using System.Text.Json;
using System.IO;

namespace AiDataGateway.Desktop;

internal sealed record DesktopSettings
{
    public bool MemoryOverlayEnabled { get; init; }
    public int? MemoryOverlayX { get; init; }
    public int? MemoryOverlayY { get; init; }
}

internal sealed class DesktopSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public DesktopSettingsStore(string storagePath)
    {
        _settingsPath = Path.Combine(Path.GetFullPath(storagePath), "desktop.settings.json");
    }

    public DesktopSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return new DesktopSettings();
            return JsonSerializer.Deserialize<DesktopSettings>(File.ReadAllText(_settingsPath), JsonOptions)
                ?? new DesktopSettings();
        }
        catch (JsonException)
        {
            return new DesktopSettings();
        }
        catch (IOException)
        {
            return new DesktopSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new DesktopSettings();
        }
    }

    public void Save(DesktopSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsPath)
            ?? throw new InvalidOperationException("无法确定桌面设置保存目录。");
        Directory.CreateDirectory(directory);
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
