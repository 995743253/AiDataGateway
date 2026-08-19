using AiDataGateway.Application.Abstractions;
using AiDataGateway.Domain.DataSources;

namespace AiDataGateway.Application.Sql;

public sealed class QueryService(
    IDataSourceRepository repository,
    ICredentialProtector credentialProtector,
    IDatabaseAdapterFactory adapterFactory,
    ISqlSafetyAnalyzer analyzer,
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

        var connection = new DatabaseConnection(source.Host, source.Port, source.Database, source.Username,
            credentialProtector.Unprotect(source.ProtectedPassword), source.CommandTimeoutSeconds);

        try
        {
            var result = await adapterFactory.Get(source.Provider).QueryAsync(connection, sql, source.MaxRows, cancellationToken);
            await auditWriter.WriteAsync(actor, "query.execute", "success", source.Id,
                $"operation={analysis.Operation};rows={result.Rows.Count};truncated={result.Truncated}", cancellationToken);
            return result;
        }
        catch (Exception exception)
        {
            await auditWriter.WriteAsync(actor, "query.execute", "failure", source.Id, exception.Message, cancellationToken);
            throw;
        }
    }
}
