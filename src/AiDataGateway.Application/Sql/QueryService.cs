using AiDataGateway.Application.Abstractions;
using AiDataGateway.Domain.DataSources;
using System.Text.Json;

namespace AiDataGateway.Application.Sql;

public sealed class QueryService(
    IDataSourceRepository repository,
    ICredentialProtector credentialProtector,
    IDatabaseAdapterFactory adapterFactory,
    ISqlSafetyAnalyzer analyzer,
    ISqlTableAccessGuard tableAccessGuard,
    IAuditWriter auditWriter)
{
    public SqlAnalysis Validate(string sql) => analyzer.Analyze(sql);

    public async Task<QueryResult> ExecuteReadAsync(Guid dataSourceId, string sql, string actor, CancellationToken cancellationToken = default)
    {
        var analysis = analyzer.Analyze(sql);
        if (!analysis.Allowed || !analysis.IsReadOnly)
        {
            throw new InvalidOperationException(string.Join(" ", analysis.Reasons.DefaultIfEmpty("Only read-only SQL is allowed here.")));
        }

        var source = await repository.FindAsync(dataSourceId, cancellationToken)
            ?? throw new KeyNotFoundException("Data source was not found.");
        if (!source.Enabled || source.AccessMode == DataSourceAccessMode.Disabled)
        {
            throw new InvalidOperationException("The data source is disabled.");
        }

        var blockedTables = tableAccessGuard.FindBlockedTables(sql, source.GetBlockedTables());
        if (blockedTables.Count > 0)
        {
            var message = $"查询已被数据源表黑名单拦截：{string.Join(", ", blockedTables)}";
            await auditWriter.WriteAsync(actor, "query.blocked", "failure", source.Id,
                JsonSerializer.Serialize(new { sql, blockedTables, error = message }), cancellationToken);
            throw new InvalidOperationException(message);
        }

        var connection = new DatabaseConnection(source.Host, source.Port, source.Database, source.Username,
            credentialProtector.Unprotect(source.ProtectedPassword), source.CommandTimeoutSeconds);

        try
        {
            var result = await adapterFactory.Get(source.Provider).QueryAsync(connection, sql, source.MaxRows, cancellationToken);
            await auditWriter.WriteAsync(actor, "query.execute", "success", source.Id,
                JsonSerializer.Serialize(new
                {
                    sql,
                    operation = analysis.Operation,
                    rowCount = result.Rows.Count,
                    truncated = result.Truncated,
                    columns = result.Columns,
                    rows = result.Rows
                }), cancellationToken);
            return result;
        }
        catch (Exception exception)
        {
            await auditWriter.WriteAsync(actor, "query.execute", "failure", source.Id,
                JsonSerializer.Serialize(new
                {
                    sql,
                    operation = analysis.Operation,
                    error = exception.Message
                }), cancellationToken);
            throw;
        }
    }
}
