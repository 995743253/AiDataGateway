namespace AiDataGateway.Domain.Monitoring;

public sealed class MonitorTargetDefinition
{
    public const string DefaultMetricSelection = "cpu.percent,memory.percent,memory.used_bytes,disk.percent,disk.used_bytes,network.received_total_bytes,network.sent_total_bytes,process.working_set_bytes,system.uptime_seconds";

    private MonitorTargetDefinition()
    {
    }

    public MonitorTargetDefinition(string key, string name, MonitorTargetType type, bool enabled = true, string? metricSelection = null)
    {
        Id = Guid.NewGuid();
        CreatedAtUtc = DateTimeOffset.UtcNow;
        Update(key, name, enabled);
        Type = type;
        SetMetricSelection(metricSelection ?? DefaultMetricSelection);
    }

    public Guid Id { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public MonitorTargetType Type { get; private set; }
    public string IngestSecretHash { get; private set; } = string.Empty;
    public bool Enabled { get; private set; }
    public string HostName { get; private set; } = string.Empty;
    public string OsDescription { get; private set; } = string.Empty;
    public string MetricSelection { get; private set; } = DefaultMetricSelection;
    public DateTimeOffset? LastSeenAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void Update(string key, string name, bool enabled)
    {
        Key = Require(key, nameof(key), 100).ToLowerInvariant();
        Name = Require(name, nameof(name), 200);
        Enabled = enabled;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void SetIngestSecretHash(string hash)
    {
        if (Type != MonitorTargetType.Remote) throw new InvalidOperationException("Local target does not accept an ingest secret.");
        IngestSecretHash = Require(hash, nameof(hash), 200);
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void SetMetricSelection(string metricSelection)
    {
        MetricSelection = Require(metricSelection, nameof(metricSelection), 4000);
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void MarkSeen(string hostName, string osDescription, DateTimeOffset collectedAtUtc)
    {
        HostName = (hostName ?? string.Empty).Trim()[..Math.Min((hostName ?? string.Empty).Trim().Length, 200)];
        OsDescription = (osDescription ?? string.Empty).Trim()[..Math.Min((osDescription ?? string.Empty).Trim().Length, 500)];
        LastSeenAtUtc = collectedAtUtc;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static string Require(string value, string parameterName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value is required.", parameterName);
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : throw new ArgumentException($"Value cannot exceed {maxLength} characters.", parameterName);
    }
}
