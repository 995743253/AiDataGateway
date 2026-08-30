using System.Text.Json;
using AiDataGateway.Application.Abstractions;
using AiDataGateway.Domain.Projects;

namespace AiDataGateway.Application.Projects;

public sealed class ProjectService(
    IProjectRepository projects,
    IDataSourceRepository dataSources,
    ILogSourceRepository logSources,
    IMonitoringRepository monitoring,
    IAuditWriter auditWriter)
{
    public async Task<IReadOnlyList<ProjectView>> ListAsync(CancellationToken cancellationToken = default)
    {
        var projectItems = await projects.ListAsync(cancellationToken);
        var sourceItems = (await dataSources.ListAsync(cancellationToken)).ToDictionary(item => item.Id);
        var logItems = (await logSources.ListAsync(cancellationToken)).ToDictionary(item => item.Id);
        var monitorItems = (await monitoring.ListTargetsAsync(cancellationToken)).ToDictionary(item => item.Id);
        var result = new List<ProjectView>(projectItems.Count);
        foreach (var project in projectItems)
        {
            var sourceIds = await projects.ListDataSourceIdsAsync(project.Id, cancellationToken);
            var logSourceIds = await projects.ListLogSourceIdsAsync(project.Id, cancellationToken);
            var monitorTargetIds = await projects.ListMonitorTargetIdsAsync(project.Id, cancellationToken);
            result.Add(ToView(project, sourceIds, logSourceIds, monitorTargetIds, sourceItems, logItems, monitorItems));
        }

        return result;
    }

    public async Task<ProjectView> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var project = await projects.FindAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException("Project was not found.");
        return await BuildViewAsync(project, cancellationToken);
    }

    public async Task<ProjectView> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var project = await projects.FindByCodeAsync(code, cancellationToken)
            ?? throw new KeyNotFoundException("Project was not found.");
        return await BuildViewAsync(project, cancellationToken);
    }

    public async Task<ProjectView> CreateAsync(ProjectUpsertRequest request, string actor, CancellationToken cancellationToken = default)
    {
        if (await projects.FindByCodeAsync(request.Code, cancellationToken) is not null)
        {
            throw new InvalidOperationException($"Project code '{request.Code}' already exists.");
        }

        var sourceIds = await ValidateDataSourcesAsync(request.DataSourceIds, cancellationToken);
        var logSourceIds = await ValidateLogSourcesAsync(request.LogSourceIds, cancellationToken);
        var monitorTargetIds = await ValidateMonitorTargetsAsync(request.MonitorTargetIds, cancellationToken);
        var project = new ProjectDefinition(request.Code, request.Name, request.Description, request.Enabled);
        await projects.AddAsync(project, cancellationToken);
        await projects.ReplaceDataSourcesAsync(project.Id, sourceIds, cancellationToken);
        await projects.ReplaceLogSourcesAsync(project.Id, logSourceIds, cancellationToken);
        await projects.ReplaceMonitorTargetsAsync(project.Id, monitorTargetIds, cancellationToken);
        await projects.SaveChangesAsync(cancellationToken);
        await auditWriter.WriteAsync(actor, "project.create", "success", detail: ProjectDetail(project), cancellationToken: cancellationToken);
        return await BuildViewAsync(project, cancellationToken);
    }

    public async Task<ProjectView> UpdateAsync(Guid id, ProjectUpsertRequest request, string actor, CancellationToken cancellationToken = default)
    {
        var project = await projects.FindAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException("Project was not found.");
        var duplicate = await projects.FindByCodeAsync(request.Code, cancellationToken);
        if (duplicate is not null && duplicate.Id != id)
        {
            throw new InvalidOperationException($"Project code '{request.Code}' already exists.");
        }

        var sourceIds = await ValidateDataSourcesAsync(request.DataSourceIds, cancellationToken);
        var logSourceIds = await ValidateLogSourcesAsync(request.LogSourceIds, cancellationToken);
        var monitorTargetIds = await ValidateMonitorTargetsAsync(request.MonitorTargetIds, cancellationToken);
        project.Update(request.Code, request.Name, request.Description, request.Enabled);
        await projects.ReplaceDataSourcesAsync(project.Id, sourceIds, cancellationToken);
        await projects.ReplaceLogSourcesAsync(project.Id, logSourceIds, cancellationToken);
        await projects.ReplaceMonitorTargetsAsync(project.Id, monitorTargetIds, cancellationToken);
        await projects.SaveChangesAsync(cancellationToken);
        await auditWriter.WriteAsync(actor, "project.update", "success", detail: ProjectDetail(project), cancellationToken: cancellationToken);
        return await BuildViewAsync(project, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, string actor, CancellationToken cancellationToken = default)
    {
        var project = await projects.FindAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException("Project was not found.");
        var detail = ProjectDetail(project);
        await projects.DeleteAsync(project, cancellationToken);
        await projects.SaveChangesAsync(cancellationToken);
        await auditWriter.WriteAsync(actor, "project.delete", "success", detail: detail, cancellationToken: cancellationToken);
    }

    private async Task<ProjectView> BuildViewAsync(ProjectDefinition project, CancellationToken cancellationToken)
    {
        var sourceIds = await projects.ListDataSourceIdsAsync(project.Id, cancellationToken);
        var logSourceIds = await projects.ListLogSourceIdsAsync(project.Id, cancellationToken);
        var monitorTargetIds = await projects.ListMonitorTargetIdsAsync(project.Id, cancellationToken);
        var sourceItems = (await dataSources.ListAsync(cancellationToken)).ToDictionary(item => item.Id);
        var logItems = (await logSources.ListAsync(cancellationToken)).ToDictionary(item => item.Id);
        var monitorItems = (await monitoring.ListTargetsAsync(cancellationToken)).ToDictionary(item => item.Id);
        return ToView(project, sourceIds, logSourceIds, monitorTargetIds, sourceItems, logItems, monitorItems);
    }

    private async Task<IReadOnlyList<Guid>> ValidateDataSourcesAsync(IEnumerable<Guid>? requestedIds, CancellationToken cancellationToken)
    {
        var ids = (requestedIds ?? []).Where(id => id != Guid.Empty).Distinct().ToArray();
        foreach (var id in ids)
        {
            if (await dataSources.FindAsync(id, cancellationToken) is null)
            {
                throw new KeyNotFoundException($"Data source '{id}' was not found.");
            }
        }

        return ids;
    }

    private async Task<IReadOnlyList<Guid>> ValidateLogSourcesAsync(IEnumerable<Guid>? requestedIds, CancellationToken cancellationToken)
    {
        var ids = (requestedIds ?? []).Where(id => id != Guid.Empty).Distinct().ToArray();
        foreach (var id in ids)
        {
            if (await logSources.FindAsync(id, cancellationToken) is null)
            {
                throw new KeyNotFoundException($"Log source '{id}' was not found.");
            }
        }

        return ids;
    }

    private async Task<IReadOnlyList<Guid>> ValidateMonitorTargetsAsync(IEnumerable<Guid>? requestedIds, CancellationToken cancellationToken)
    {
        var ids = (requestedIds ?? []).Where(id => id != Guid.Empty).Distinct().ToArray();
        foreach (var id in ids)
        {
            if (await monitoring.FindTargetAsync(id, cancellationToken) is null)
            {
                throw new KeyNotFoundException($"Monitor target '{id}' was not found.");
            }
        }

        return ids;
    }

    private static ProjectView ToView(
        ProjectDefinition project,
        IEnumerable<Guid> sourceIds,
        IEnumerable<Guid> logSourceIds,
        IEnumerable<Guid> monitorTargetIds,
        IReadOnlyDictionary<Guid, Domain.DataSources.DataSourceDefinition> sourceItems,
        IReadOnlyDictionary<Guid, Domain.Logs.LogSourceDefinition> logItems,
        IReadOnlyDictionary<Guid, Domain.Monitoring.MonitorTargetDefinition> monitorItems) =>
        new(
            project.Id,
            project.Code,
            project.Name,
            project.Description,
            project.Enabled,
            sourceIds.Where(sourceItems.ContainsKey).Select(id => sourceItems[id]).Select(item => new ProjectDataSourceReference(
                item.Id, item.Key, item.Name, item.Provider.ToString(), item.Enabled)).ToArray(),
            logSourceIds.Where(logItems.ContainsKey).Select(id => logItems[id]).Select(item => new ProjectLogSourceReference(
                item.Id, item.Key, item.Name, item.Type.ToString(), item.Enabled)).ToArray(),
            monitorTargetIds.Where(monitorItems.ContainsKey).Select(id => monitorItems[id]).Select(item => new ProjectMonitorTargetReference(
                item.Id, item.Key, item.Name, item.Type.ToString(), item.Enabled)).ToArray(),
            project.UpdatedAtUtc);

    private static string ProjectDetail(ProjectDefinition project) => JsonSerializer.Serialize(new
    {
        project.Id,
        project.Code,
        project.Name
    });
}
