namespace AiDataGateway.Domain.Logs;

public sealed class LogSourceDefinition
{
    private LogSourceDefinition()
    {
    }

    public LogSourceDefinition(
        string key,
        string name,
        LogSourceType type,
        string endpoint,
        string? nlogTargetName,
        string? nlogLayout,
        bool enabled)
    {
        Id = Guid.NewGuid();
        CreatedAtUtc = DateTimeOffset.UtcNow;
        Update(key, name, type, endpoint, nlogTargetName, nlogLayout, enabled);
    }

    public Guid Id { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public LogSourceType Type { get; private set; }
    public string Endpoint { get; private set; } = string.Empty;
    public string NLogTargetName { get; private set; } = string.Empty;
    public string NLogLayout { get; private set; } = string.Empty;
    public string ProtectedConfiguration { get; private set; } = string.Empty;
    public string ProtectedApiKey { get; private set; } = string.Empty;
    public bool Enabled { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void Update(
        string key,
        string name,
        LogSourceType type,
        string endpoint,
        string? nlogTargetName,
        string? nlogLayout,
        bool enabled)
    {
        Key = Require(key, nameof(key), 100).ToLowerInvariant();
        Name = Require(name, nameof(name), 200);
        Type = Enum.IsDefined(type) ? type : throw new ArgumentOutOfRangeException(nameof(type));
        Endpoint = (endpoint ?? string.Empty).Trim();
        if (Endpoint.Length > 2_000)
        {
            throw new ArgumentException("Endpoint cannot exceed 2000 characters.", nameof(endpoint));
        }

        NLogTargetName = (nlogTargetName ?? string.Empty).Trim();
        NLogLayout = (nlogLayout ?? string.Empty).Trim();
        if (NLogTargetName.Length > 200 || NLogLayout.Length > 4_000)
        {
            throw new ArgumentException("NLog target name or layout is too long.");
        }

        Enabled = enabled;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void SetProtectedConfiguration(string protectedConfiguration)
    {
        ProtectedConfiguration = protectedConfiguration ?? string.Empty;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void SetProtectedApiKey(string protectedApiKey)
    {
        ProtectedApiKey = protectedApiKey ?? string.Empty;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static string Require(string value, string parameterName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : throw new ArgumentException($"Value cannot exceed {maxLength} characters.", parameterName);
    }
}
