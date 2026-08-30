using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AiDataGateway.Application.Abstractions;
using AiDataGateway.Domain.Monitoring;
using AiDataGateway.Monitoring;

namespace AiDataGateway.Application.Monitoring;

public sealed class MonitoringService(
    IMonitoringRepository monitoring,
    IProjectRepository projects,
    IAuditWriter auditWriter,
    IGatewayEventPublisher events)
{
    public async Task<IReadOnlyList<MonitorTargetView>> ListTargetsAsync(CancellationToken cancellationToken = default)
    {
        var projectMap = (await projects.ListAsync(cancellationToken)).ToDictionary(item => item.Id);
        var targets = await monitoring.ListTargetsAsync(cancellationToken);
        var result = new List<MonitorTargetView>(targets.Count);
        foreach (var target in targets)
        {
            result.Add(await ToViewAsync(target, projectMap, cancellationToken));
        }

        return result;
    }

    public async Task<MonitorTargetCreatedView> CreateRemoteAsync(MonitorTargetUpsertRequest request, string actor, CancellationToken cancellationToken = default)
    {
        if (await monitoring.FindTargetByKeyAsync(request.Key, cancellationToken) is not null)
        {
            throw new InvalidOperationException($"Monitor target key '{request.Key}' already exists.");
        }

        var projectIds = await ValidateProjectsAsync(request.ProjectIds, cancellationToken);
        var selection = MetricCatalog.NormalizeSelection(request.MetricKeys);
        var target = new MonitorTargetDefinition(request.Key, request.Name, MonitorTargetType.Remote, request.Enabled, SerializeSelection(selection));
        var secret = CreateSecret();
        target.SetIngestSecretHash(HashSecret(secret));
        await monitoring.AddTargetAsync(target, cancellationToken);
        await monitoring.ReplaceProjectsAsync(target.Id, projectIds, cancellationToken);
        await monitoring.SaveChangesAsync(cancellationToken);
        await auditWriter.WriteAsync(actor, "monitor-target.create", "success", detail: TargetDetail(target), cancellationToken: cancellationToken);
        events.Publish("monitoring", "monitor-target.create");
        return new MonitorTargetCreatedView(await BuildViewAsync(target, cancellationToken), secret);
    }

    public async Task<MonitorTargetView> UpdateAsync(Guid id, MonitorTargetUpsertRequest request, string actor, CancellationToken cancellationToken = default)
    {
        var target = await monitoring.FindTargetAsync(id, cancellationToken) ?? throw new KeyNotFoundException("Monitor target was not found.");
        var duplicate = await monitoring.FindTargetByKeyAsync(request.Key, cancellationToken);
        if (duplicate is not null && duplicate.Id != id) throw new InvalidOperationException($"Monitor target key '{request.Key}' already exists.");
        var projectIds = await ValidateProjectsAsync(request.ProjectIds, cancellationToken);
        target.Update(request.Key, request.Name, request.Enabled);
        target.SetMetricSelection(SerializeSelection(request.MetricKeys is null ? ParseSelection(target.MetricSelection) : MetricCatalog.NormalizeSelection(request.MetricKeys)));
        await monitoring.ReplaceProjectsAsync(target.Id, projectIds, cancellationToken);
        await monitoring.SaveChangesAsync(cancellationToken);
        await auditWriter.WriteAsync(actor, "monitor-target.update", "success", detail: TargetDetail(target), cancellationToken: cancellationToken);
        events.Publish("monitoring", "monitor-target.update");
        return await BuildViewAsync(target, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, string actor, CancellationToken cancellationToken = default)
    {
        var target = await monitoring.FindTargetAsync(id, cancellationToken) ?? throw new KeyNotFoundException("Monitor target was not found.");
        if (target.Type == MonitorTargetType.Local) throw new InvalidOperationException("The built-in local monitor cannot be deleted.");
        var detail = TargetDetail(target);
        await monitoring.DeleteTargetAsync(target, cancellationToken);
        await monitoring.SaveChangesAsync(cancellationToken);
        await auditWriter.WriteAsync(actor, "monitor-target.delete", "success", detail: detail, cancellationToken: cancellationToken);
        events.Publish("monitoring", "monitor-target.delete");
    }

    public async Task<MonitorSecretRotatedView> RotateSecretAsync(Guid id, string actor, CancellationToken cancellationToken = default)
    {
        var target = await monitoring.FindTargetAsync(id, cancellationToken) ?? throw new KeyNotFoundException("Monitor target was not found.");
        if (target.Type != MonitorTargetType.Remote) throw new InvalidOperationException("The local monitor does not use an ingest secret.");
        var secret = CreateSecret();
        target.SetIngestSecretHash(HashSecret(secret));
        await monitoring.SaveChangesAsync(cancellationToken);
        await auditWriter.WriteAsync(actor, "monitor-target.rotate-secret", "success", detail: TargetDetail(target), cancellationToken: cancellationToken);
        events.Publish("monitoring", "monitor-target.rotate-secret");
        return new MonitorSecretRotatedView(target.Id, target.Key, secret);
    }

    public async Task<bool> IngestRemoteAsync(string key, string? secret, MetricIngestRequest request, CancellationToken cancellationToken = default)
    {
        var target = await monitoring.FindTargetByKeyAsync(key, cancellationToken);
        if (!CanIngest(target, secret)) return false;

        ValidateSample(request);
        var collectedAt = NormalizeTimestamp(request.CollectedAtUtc);
        var selection = ParseSelection(target!.MetricSelection);
        target.MarkSeen(request.HostName, request.OsDescription, collectedAt);
        await monitoring.AddSampleAsync(ToSample(target.Id, request, collectedAt, selection), cancellationToken);
        await monitoring.SaveChangesAsync(cancellationToken);
        events.Publish("monitoring", "metrics.ingested");
        return true;
    }

    public async Task RecordLocalAsync(MetricIngestRequest request, CancellationToken cancellationToken = default)
    {
        var target = await monitoring.FindTargetByKeyAsync("local", cancellationToken)
            ?? throw new InvalidOperationException("The built-in local monitor is missing.");
        if (!target.Enabled) return;
        ValidateSample(request);
        var collectedAt = NormalizeTimestamp(request.CollectedAtUtc);
        var selection = ParseSelection(target.MetricSelection);
        target.MarkSeen(request.HostName, request.OsDescription, collectedAt);
        await monitoring.AddSampleAsync(ToSample(target.Id, request, collectedAt, selection), cancellationToken);
        await monitoring.SaveChangesAsync(cancellationToken);
        events.Publish("monitoring", "metrics.ingested");
    }

    public MetricCatalogView GetMetricCatalog() => new(MetricCatalog.All, MetricCatalog.RequiredKeys, MetricCatalog.DefaultKeys);

    public async Task<IReadOnlyList<string>> GetLocalMetricKeysAsync(CancellationToken cancellationToken = default)
    {
        var target = await monitoring.FindTargetByKeyAsync("local", cancellationToken)
            ?? throw new InvalidOperationException("The built-in local monitor is missing.");
        return ParseSelection(target.MetricSelection).ToArray();
    }

    public async Task<MetricIngestConfigurationView?> GetIngestConfigurationAsync(string key, string? secret, CancellationToken cancellationToken = default)
    {
        var target = await monitoring.FindTargetByKeyAsync(key, cancellationToken);
        return CanIngest(target, secret)
            ? new MetricIngestConfigurationView(target!.Key, ParseSelection(target.MetricSelection).ToArray())
            : null;
    }

    public async Task<MetricQueryView> QueryAsync(Guid targetId, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var target = await monitoring.FindTargetAsync(targetId, cancellationToken) ?? throw new KeyNotFoundException("Monitor target was not found.");
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);
        var result = await monitoring.QuerySamplesAsync(target.Id, fromUtc, toUtc, page, pageSize, cancellationToken);
        return new MetricQueryView(target.Id, target.Key, target.Name, result.Items.Select(ToSampleView).ToArray(), page, pageSize, result.Total);
    }

    public async Task<MetricTrendView> QueryTrendAsync(Guid targetId, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, int maxPoints, CancellationToken cancellationToken = default)
    {
        var target = await monitoring.FindTargetAsync(targetId, cancellationToken) ?? throw new KeyNotFoundException("Monitor target was not found.");
        var to = (toUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var from = (fromUtc ?? to.AddHours(-1)).ToUniversalTime();
        if (from >= to) throw new ArgumentException("Trend start time must be earlier than end time.");
        if (to - from > TimeSpan.FromDays(31)) throw new ArgumentException("A single historical trend query cannot exceed 31 days.");
        maxPoints = Math.Clamp(maxPoints, 50, 800);
        var result = await monitoring.QueryTrendSamplesAsync(target.Id, from, to, maxPoints, cancellationToken);
        return new MetricTrendView(target.Id, target.Key, target.Name, from, to, result.Total, result.Items.Select(ToSampleView).ToArray());
    }

    public async Task<MetricQueryView> QueryByProjectAsync(string projectCode, string? targetKey, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, int count, CancellationToken cancellationToken = default)
    {
        var project = await projects.FindByCodeAsync(projectCode, cancellationToken) ?? throw new KeyNotFoundException("Project was not found.");
        if (!project.Enabled) throw new InvalidOperationException("Project is disabled.");
        var targetIds = await projects.ListMonitorTargetIdsAsync(project.Id, cancellationToken);
        var targetItems = await monitoring.ListTargetsAsync(cancellationToken);
        var target = string.IsNullOrWhiteSpace(targetKey)
            ? targetItems.FirstOrDefault(item => targetIds.Contains(item.Id) && item.Enabled)
            : targetItems.FirstOrDefault(item => targetIds.Contains(item.Id) && item.Enabled && item.Key == targetKey.Trim().ToLowerInvariant());
        if (target is null) throw new KeyNotFoundException("No matching enabled monitor target is linked to this project.");
        return await QueryAsync(target.Id, fromUtc, toUtc, 1, Math.Clamp(count, 1, 500), cancellationToken);
    }

    private async Task<MonitorTargetView> BuildViewAsync(MonitorTargetDefinition target, CancellationToken cancellationToken)
    {
        var projectMap = (await projects.ListAsync(cancellationToken)).ToDictionary(item => item.Id);
        return await ToViewAsync(target, projectMap, cancellationToken);
    }

    private async Task<MonitorTargetView> ToViewAsync(MonitorTargetDefinition target, IReadOnlyDictionary<Guid, Domain.Projects.ProjectDefinition> projectMap, CancellationToken cancellationToken)
    {
        var projectIds = await monitoring.ListProjectIdsAsync(target.Id, cancellationToken);
        var latest = await monitoring.LatestSampleAsync(target.Id, cancellationToken);
        return new MonitorTargetView(
            target.Id, target.Key, target.Name, target.Type, target.Enabled, target.HostName, target.OsDescription,
            target.LastSeenAtUtc, target.Enabled && target.LastSeenAtUtc >= DateTimeOffset.UtcNow.AddMinutes(-2),
            ParseSelection(target.MetricSelection).ToArray(),
            projectIds.Where(projectMap.ContainsKey).Select(id => projectMap[id]).Select(project => new MonitorTargetProjectReference(project.Id, project.Code, project.Name, project.Enabled)).ToArray(),
            latest is null ? null : ToSampleView(latest), target.UpdatedAtUtc);
    }

    private async Task<IReadOnlyList<Guid>> ValidateProjectsAsync(IEnumerable<Guid>? values, CancellationToken cancellationToken)
    {
        var ids = (values ?? []).Where(id => id != Guid.Empty).Distinct().ToArray();
        foreach (var id in ids)
        {
            if (await projects.FindAsync(id, cancellationToken) is null) throw new KeyNotFoundException($"Project '{id}' was not found.");
        }
        return ids;
    }

    private static void ValidateSample(MetricIngestRequest request)
    {
        if (!double.IsFinite(request.CpuPercent) || request.CpuPercent is < 0 or > 100) throw new ArgumentException("CPU percent must be between 0 and 100.");
        if (request.MemoryUsedBytes < 0 || request.MemoryTotalBytes < 0 || request.MemoryUsedBytes > request.MemoryTotalBytes) throw new ArgumentException("Memory counters are invalid.");
        if (request.DiskUsedBytes < 0 || request.DiskTotalBytes < 0 || request.DiskUsedBytes > request.DiskTotalBytes) throw new ArgumentException("Disk counters are invalid.");
        if (request.ExtendedMetrics is { Count: > 100 }) throw new ArgumentException("Too many extended metrics were submitted.");
        if (request.ExtendedMetrics?.Any(item => !double.IsFinite(item.Value) || item.Value < 0) == true) throw new ArgumentException("Extended metrics must be finite and non-negative.");
    }

    private static DateTimeOffset NormalizeTimestamp(DateTimeOffset value)
    {
        var now = DateTimeOffset.UtcNow;
        if (value == default) return now;
        if (value < now.AddDays(-1) || value > now.AddMinutes(5)) throw new ArgumentException("Collected timestamp is outside the accepted range.");
        return value.ToUniversalTime();
    }

    private static ServerMetricSample ToSample(Guid targetId, MetricIngestRequest value, DateTimeOffset collectedAt, IReadOnlySet<string> selection) =>
        new(targetId, collectedAt, value.CpuPercent, value.MemoryUsedBytes, value.MemoryTotalBytes, value.DiskUsedBytes, value.DiskTotalBytes,
            value.NetworkReceivedBytes, value.NetworkSentBytes, value.ProcessWorkingSetBytes, value.SystemUptimeSeconds,
            JsonSerializer.Serialize((value.ExtendedMetrics ?? new Dictionary<string, double>())
                .Where(item => selection.Contains(item.Key) && MetricCatalog.KnownKeys.Contains(item.Key))
                .ToDictionary(item => item.Key.ToLowerInvariant(), item => Math.Round(item.Value, 2), StringComparer.OrdinalIgnoreCase)));

    private static MetricSampleView ToSampleView(ServerMetricSample value)
    {
        Dictionary<string, double> metrics;
        try { metrics = JsonSerializer.Deserialize<Dictionary<string, double>>(value.ExtendedMetricsJson) ?? new Dictionary<string, double>(); }
        catch (JsonException) { metrics = new Dictionary<string, double>(); }
        metrics["cpu.percent"] = value.CpuPercent;
        metrics["memory.percent"] = Percent(value.MemoryUsedBytes, value.MemoryTotalBytes);
        metrics["memory.used_bytes"] = value.MemoryUsedBytes;
        metrics["disk.percent"] = Percent(value.DiskUsedBytes, value.DiskTotalBytes);
        metrics["disk.used_bytes"] = value.DiskUsedBytes;
        metrics["network.received_total_bytes"] = value.NetworkReceivedBytes;
        metrics["network.sent_total_bytes"] = value.NetworkSentBytes;
        metrics["process.working_set_bytes"] = value.ProcessWorkingSetBytes;
        metrics["system.uptime_seconds"] = value.SystemUptimeSeconds;
        return new MetricSampleView(
            value.Id, value.CollectedAtUtc, value.CpuPercent, value.MemoryUsedBytes, value.MemoryTotalBytes, Percent(value.MemoryUsedBytes, value.MemoryTotalBytes),
            value.DiskUsedBytes, value.DiskTotalBytes, Percent(value.DiskUsedBytes, value.DiskTotalBytes), value.NetworkReceivedBytes, value.NetworkSentBytes,
            value.ProcessWorkingSetBytes, value.SystemUptimeSeconds, metrics);
    }

    private static double Percent(long used, long total) => total <= 0 ? 0 : Math.Round(Math.Clamp(used * 100d / total, 0, 100), 2);
    private static string CreateSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private static string HashSecret(string secret) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
    private static bool FixedEquals(string left, string right)
    {
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(left), Convert.FromBase64String(right)); }
        catch (FormatException) { return false; }
    }
    private static bool CanIngest(MonitorTargetDefinition? target, string? secret) =>
        target is not null && target.Type == MonitorTargetType.Remote && target.Enabled && !string.IsNullOrWhiteSpace(secret) &&
        FixedEquals(target.IngestSecretHash, HashSecret(secret));
    private static HashSet<string> ParseSelection(string value) => MetricCatalog.NormalizeSelection(value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToHashSet(StringComparer.OrdinalIgnoreCase);
    private static string SerializeSelection(IEnumerable<string> values) => string.Join(',', MetricCatalog.NormalizeSelection(values));
    private static string TargetDetail(MonitorTargetDefinition target) => JsonSerializer.Serialize(new { target.Id, target.Key, target.Name, type = target.Type.ToString(), metricKeys = ParseSelection(target.MetricSelection) });
}
