using AiDataGateway.Application.Abstractions;
using AiDataGateway.Application.Sql;

namespace AiDataGateway.Application.Logs;

public sealed class LogSqlTraceService(
    ILogSourceRepository logSources,
    IProjectRepository projects,
    IDataSourceRepository dataSources,
    QueryService queries)
{
    public async Task<IReadOnlyList<LogSqlProjectView>> ListProjectsAsync(
        Guid logSourceId,
        CancellationToken cancellationToken = default)
    {
        var source = await logSources.FindAsync(logSourceId, cancellationToken)
            ?? throw new KeyNotFoundException("Log source was not found.");
        if (!source.Enabled) throw new InvalidOperationException("Log source is disabled.");

        var result = new List<LogSqlProjectView>();
        foreach (var projectId in await logSources.ListProjectIdsAsync(logSourceId, cancellationToken))
        {
            var project = await projects.FindAsync(projectId, cancellationToken);
            if (project is null || !project.Enabled) continue;

            var linkedSources = new List<LogSqlDataSourceView>();
            foreach (var dataSourceId in await projects.ListDataSourceIdsAsync(projectId, cancellationToken))
            {
                var dataSource = await dataSources.FindAsync(dataSourceId, cancellationToken);
                if (dataSource is null || !dataSource.Enabled) continue;
                linkedSources.Add(new LogSqlDataSourceView(dataSource.Id, dataSource.Key, dataSource.Name,
                    dataSource.Provider.ToString()));
            }

            result.Add(new LogSqlProjectView(project.Id, project.Code, project.Name, linkedSources));
        }

        return result.OrderBy(item => item.Code, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<QueryResult> ExecuteReadAsync(
        Guid logSourceId,
        LogSqlQueryRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var logSource = await logSources.FindAsync(logSourceId, cancellationToken)
            ?? throw new KeyNotFoundException("Log source was not found.");
        if (!logSource.Enabled) throw new InvalidOperationException("Log source is disabled.");

        var projectIds = await logSources.ListProjectIdsAsync(logSourceId, cancellationToken);
        if (!projectIds.Contains(request.ProjectId))
            throw new InvalidOperationException("The selected project is not linked to this log source.");

        var project = await projects.FindAsync(request.ProjectId, cancellationToken)
            ?? throw new KeyNotFoundException("Project was not found.");
        if (!project.Enabled) throw new InvalidOperationException("Project is disabled.");

        var dataSourceIds = await projects.ListDataSourceIdsAsync(request.ProjectId, cancellationToken);
        if (!dataSourceIds.Contains(request.DataSourceId))
            throw new InvalidOperationException("The selected data source is not linked to this project.");

        return await queries.ExecuteReadAsync(request.DataSourceId, request.Sql, actor, cancellationToken);
    }
}
