using System.Text.Json;
using System.Runtime.CompilerServices;
using AiDataGateway.Application.Abstractions;
using AiDataGateway.Domain.Logs;
using AiDataGateway.Domain.Projects;

namespace AiDataGateway.Application.Logs;

public sealed class LogSourceService(
    ILogSourceRepository logSources,
    IProjectRepository projects,
    ICredentialProtector protector,
    ILogSourceAdapterFactory adapterFactory,
    IAuditWriter auditWriter)
{
    public async Task<IReadOnlyList<LogSourceView>> ListAsync(bool includeConfiguration = true, CancellationToken cancellationToken = default)
    {
        var sources = await logSources.ListAsync(cancellationToken);
        var projectItems = (await projects.ListAsync(cancellationToken)).ToDictionary(item => item.Id);
        var result = new List<LogSourceView>(sources.Count);
        foreach (var source in sources)
        {
            var projectIds = await logSources.ListProjectIdsAsync(source.Id, cancellationToken);
            result.Add(ToView(source, projectIds, projectItems, includeConfiguration));
        }

        return result;
    }

    public async Task<LogSourceView> CreateAsync(LogSourceUpsertRequest request, string actor, CancellationToken cancellationToken = default)
    {
        if (await logSources.FindByKeyAsync(request.Key, cancellationToken) is not null)
        {
            throw new InvalidOperationException($"Log source key '{request.Key}' already exists.");
        }

        Validate(request);
        var projectItems = await ValidateProjectsAsync(request.ProjectIds, cancellationToken);
        var source = new LogSourceDefinition(request.Key, request.Name, request.Type,
            request.Endpoint ?? string.Empty, request.NLogTargetName, request.NLogLayout, request.Enabled);
        SetSecrets(source, request, creating: true);
        await logSources.AddAsync(source, cancellationToken);
        await logSources.ReplaceProjectsAsync(source.Id, projectItems.Keys, cancellationToken);
        await logSources.SaveChangesAsync(cancellationToken);
        await auditWriter.WriteAsync(actor, "logsource.create", "success", detail: Detail(source, projectItems.Keys), cancellationToken: cancellationToken);
        return ToView(source, projectItems.Keys, projectItems, true);
    }

    public async Task<LogSourceView> UpdateAsync(Guid id, LogSourceUpsertRequest request, string actor, CancellationToken cancellationToken = default)
    {
        var source = await logSources.FindAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException("Log source was not found.");
        var duplicate = await logSources.FindByKeyAsync(request.Key, cancellationToken);
        if (duplicate is not null && duplicate.Id != id)
        {
            throw new InvalidOperationException($"Log source key '{request.Key}' already exists.");
        }

        Validate(request);
        var projectItems = await ValidateProjectsAsync(request.ProjectIds, cancellationToken);
        source.Update(request.Key, request.Name, request.Type, request.Endpoint ?? string.Empty,
            request.NLogTargetName, request.NLogLayout, request.Enabled);
        SetSecrets(source, request, creating: false);
        await logSources.ReplaceProjectsAsync(source.Id, projectItems.Keys, cancellationToken);
        await logSources.SaveChangesAsync(cancellationToken);
        await auditWriter.WriteAsync(actor, "logsource.update", "success", detail: Detail(source, projectItems.Keys), cancellationToken: cancellationToken);
        return ToView(source, projectItems.Keys, projectItems, true);
    }

    public async Task DeleteAsync(Guid id, string actor, CancellationToken cancellationToken = default)
    {
        var source = await logSources.FindAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException("Log source was not found.");
        var projectIds = await logSources.ListProjectIdsAsync(source.Id, cancellationToken);
        var detail = Detail(source, projectIds);
        await logSources.DeleteAsync(source, cancellationToken);
        await logSources.SaveChangesAsync(cancellationToken);
        await auditWriter.WriteAsync(actor, "logsource.delete", "success", detail: detail, cancellationToken: cancellationToken);
    }

    public async Task<LogSourceTestResult> TestAsync(Guid id, string actor, CancellationToken cancellationToken = default)
    {
        var source = await logSources.FindAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException("Log source was not found.");
        var result = await adapterFactory.Get(source.Type).TestAsync(ToConnection(source), cancellationToken);
        await auditWriter.WriteAsync(actor, "logsource.test", result.Success ? "success" : "failure",
            detail: JsonSerializer.Serialize(new { source.Id, source.Key, result.Message }), cancellationToken: cancellationToken);
        return result;
    }

    public async Task<LogQueryView> QueryAsync(LogQueryRequest request, string actor, CancellationToken cancellationToken = default)
    {
        var source = await logSources.FindAsync(request.LogSourceId, cancellationToken)
            ?? throw new KeyNotFoundException("Log source was not found.");
        var projectIds = await logSources.ListProjectIdsAsync(source.Id, cancellationToken);
        var project = projectIds.Count == 1
            ? await projects.FindAsync(projectIds[0], cancellationToken)
            : null;
        return await QuerySourceAsync(source, project, request.Query, request.Level, request.FromUtc, request.ToUtc,
            request.SearchText, request.PropertyName, request.PropertyValue,
            request.Page, request.PageSize, actor, cancellationToken);
    }

    public async IAsyncEnumerable<StructuredLogEvent> StreamAsync(
        LogQueryRequest request,
        string actor,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var source = await logSources.FindAsync(request.LogSourceId, cancellationToken)
            ?? throw new KeyNotFoundException("Log source was not found.");
        if (!source.Enabled) throw new InvalidOperationException("Log source is disabled.");
        var startedAt = DateTimeOffset.UtcNow;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        await auditWriter.WriteAsync(actor, "log.stream", "started", detail: JsonSerializer.Serialize(new
        {
            logSourceId = source.Id,
            source.Key,
            request.Level,
            request.SearchText,
            request.PropertyName
        }), cancellationToken: cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            var to = DateTimeOffset.UtcNow.AddSeconds(1);
            var from = request.FromUtc?.ToUniversalTime() ?? startedAt.AddSeconds(-2);
            var options = new LogQueryOptions(request.Query, request.Level, from, to,
                request.SearchText, request.PropertyName, request.PropertyValue, 1, 200);
            var result = await adapterFactory.Get(source.Type).QueryAsync(ToConnection(source), options, cancellationToken);
            foreach (var item in result.Items.OrderBy(value => value.TimestampUtc ?? DateTimeOffset.MinValue))
            {
                if (seen.Add(item.Id)) yield return item;
            }

            if (seen.Count > 5_000) seen.Clear();
            await Task.Delay(TimeSpan.FromSeconds(1.5), cancellationToken);
        }
    }

    public async Task<LogQueryView> QueryByProjectAsync(ProjectLogQueryRequest request, string actor, CancellationToken cancellationToken = default)
    {
        var project = await projects.FindByCodeAsync(request.ProjectCode, cancellationToken)
            ?? throw new KeyNotFoundException("Project was not found.");
        if (!project.Enabled)
        {
            throw new InvalidOperationException("Project is disabled.");
        }

        var linkedIds = await projects.ListLogSourceIdsAsync(project.Id, cancellationToken);
        var allSources = await logSources.ListAsync(cancellationToken);
        var sources = allSources.Where(item => linkedIds.Contains(item.Id) && item.Enabled).ToArray();
        var source = string.IsNullOrWhiteSpace(request.LogSourceKey)
            ? sources.Length == 1
                ? sources[0]
                : throw new InvalidOperationException("Specify logSourceKey when the project has zero or multiple enabled log sources.")
            : sources.SingleOrDefault(item => string.Equals(item.Key, request.LogSourceKey.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException("Enabled log source was not found in the project.");
        return await QuerySourceAsync(source, project, request.Query, request.Level, request.FromUtc, request.ToUtc,
            request.SearchText, request.PropertyName, request.PropertyValue,
            request.Page, request.PageSize, actor, cancellationToken);
    }

    private async Task<LogQueryView> QuerySourceAsync(
        LogSourceDefinition source,
        ProjectDefinition? project,
        string? query,
        string? level,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        string? searchText,
        string? propertyName,
        string? propertyValue,
        int page,
        int pageSize,
        string actor,
        CancellationToken cancellationToken)
    {
        if (!source.Enabled)
        {
            throw new InvalidOperationException("Log source is disabled.");
        }

        var to = (toUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var from = (fromUtc ?? to.AddDays(-7)).ToUniversalTime();
        if (from >= to) throw new ArgumentException("日志开始时间必须早于结束时间。");
        if (to - from > TimeSpan.FromDays(31)) throw new ArgumentException("单次日志查询时间范围不能超过 31 天。");
        if (!string.IsNullOrWhiteSpace(propertyValue) && string.IsNullOrWhiteSpace(propertyName))
            throw new ArgumentException("按属性查询时必须填写属性名。");

        var options = new LogQueryOptions(query, level, from, to, searchText, propertyName, propertyValue,
            Math.Clamp(page, 1, 100_000), Math.Clamp(pageSize, 1, 500));
        try
        {
            var result = await adapterFactory.Get(source.Type).QueryAsync(ToConnection(source), options, cancellationToken);
            await auditWriter.WriteAsync(actor, "log.query", "success", detail: JsonSerializer.Serialize(new
            {
                projectCode = project?.Code,
                logSourceId = source.Id,
                logSourceKey = source.Key,
                query,
                level,
                fromUtc = from,
                toUtc = to,
                searchText,
                propertyName,
                resultCount = result.Items.Count
            }), cancellationToken: cancellationToken);
            return new LogQueryView(source.Id, source.Key, source.Name, project?.Code ?? string.Empty, result.Items,
                result.Page, result.PageSize, result.Total, result.IsPartial, result.Warning);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await auditWriter.WriteAsync(actor, "log.query", "failure", detail: JsonSerializer.Serialize(new
            {
                projectCode = project?.Code,
                logSourceId = source.Id,
                logSourceKey = source.Key,
                query,
                level,
                fromUtc = from,
                toUtc = to,
                searchText,
                propertyName,
                error = exception.Message
            }), cancellationToken: cancellationToken);
            throw;
        }
    }

    private LogSourceConnection ToConnection(LogSourceDefinition source) => new(
        source.Type,
        source.Endpoint,
        Unprotect(source.ProtectedConfiguration),
        source.NLogTargetName,
        source.NLogLayout,
        Unprotect(source.ProtectedApiKey));

    private void SetSecrets(LogSourceDefinition source, LogSourceUpsertRequest request, bool creating)
    {
        if (!string.IsNullOrWhiteSpace(request.NLogConfiguration))
        {
            source.SetProtectedConfiguration(protector.Protect(request.NLogConfiguration));
        }
        else if (creating)
        {
            source.SetProtectedConfiguration(string.Empty);
        }

        if (!string.IsNullOrWhiteSpace(request.ApiKey))
        {
            source.SetProtectedApiKey(protector.Protect(request.ApiKey));
        }
        else if (creating)
        {
            source.SetProtectedApiKey(string.Empty);
        }
    }

    private string Unprotect(string protectedValue) => string.IsNullOrEmpty(protectedValue)
        ? string.Empty
        : protector.Unprotect(protectedValue);

    private LogSourceView ToView(
        LogSourceDefinition source,
        IEnumerable<Guid> projectIds,
        IReadOnlyDictionary<Guid, ProjectDefinition> projectItems,
        bool includeConfiguration) => new(
        source.Id,
        source.Key,
        source.Name,
        source.Type,
        source.Endpoint,
        includeConfiguration ? Unprotect(source.ProtectedConfiguration) : string.Empty,
        source.NLogTargetName,
        source.NLogLayout,
        !string.IsNullOrEmpty(source.ProtectedApiKey),
        source.Enabled,
        projectIds.Where(projectItems.ContainsKey).Select(id => projectItems[id]).Select(item =>
            new LogSourceProjectReference(item.Id, item.Code, item.Name, item.Enabled)).ToArray(),
        source.UpdatedAtUtc);

    private async Task<IReadOnlyDictionary<Guid, ProjectDefinition>> ValidateProjectsAsync(
        IEnumerable<Guid>? requestedIds,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, ProjectDefinition>();
        foreach (var id in (requestedIds ?? []).Where(id => id != Guid.Empty).Distinct())
        {
            result[id] = await projects.FindAsync(id, cancellationToken)
                ?? throw new KeyNotFoundException($"Project '{id}' was not found.");
        }

        return result;
    }

    private static void Validate(LogSourceUpsertRequest request)
    {
        if (request.Type is LogSourceType.Seq or LogSourceType.RemoteAgent)
        {
            if (!Uri.TryCreate(request.Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("http" or "https"))
            {
                throw new ArgumentException("Seq 或远程 Agent 地址必须是完整的 HTTP/HTTPS URL。");
            }
            if (request.Type == LogSourceType.RemoteAgent && string.IsNullOrWhiteSpace(request.ApiKey))
                throw new ArgumentException("远程 Agent 日志源必须填写访问密钥。");
        }
        else if (request.Type == LogSourceType.LocalNLog &&
                 string.IsNullOrWhiteSpace(request.Endpoint) && string.IsNullOrWhiteSpace(request.NLogConfiguration))
        {
            throw new ArgumentException("Local NLog source requires a log folder, file path/pattern, or NLog configuration.");
        }
    }

    private static string Detail(LogSourceDefinition source, IEnumerable<Guid> projectIds) => JsonSerializer.Serialize(new
    {
        source.Id,
        source.Key,
        source.Name,
        source.Type,
        projectIds
    });
}
