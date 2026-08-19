using AiDataGateway.Domain.DataSources;

namespace AiDataGateway.Application.Abstractions;

public interface IDatabaseAdapter
{
    DatabaseProvider Provider { get; }
    Task<ConnectionTestResult> TestConnectionAsync(DatabaseConnection connection, CancellationToken cancellationToken = default);
    Task<QueryResult> QueryAsync(DatabaseConnection connection, string sql, int maxRows, CancellationToken cancellationToken = default);
    Task<int> ExecuteAsync(DatabaseConnection connection, string sql, CancellationToken cancellationToken = default);
}

public interface IDatabaseAdapterFactory
{
    IDatabaseAdapter Get(DatabaseProvider provider);
}

public sealed record DatabaseConnection(
    string Host,
    int Port,
    string Database,
    string Username,
    string Password,
    int CommandTimeoutSeconds);

public sealed record ConnectionTestResult(bool Success, string Message, TimeSpan Elapsed);

public sealed record QueryResult(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows, bool Truncated);
