using System.Collections;
using System.Diagnostics;
using AiDataGateway.Application.Abstractions;
using AiDataGateway.Domain.DataSources;
using FreeSql;

namespace AiDataGateway.Infrastructure.Databases;

internal sealed class FreeSqlDatabaseAdapter(DatabaseProvider provider) : IDatabaseAdapter
{
    public DatabaseProvider Provider { get; } = provider;

    public async Task<ConnectionTestResult> TestConnectionAsync(DatabaseConnection connection, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var freeSql = Build(connection);
            await freeSql.Ado.ExecuteScalarAsync("SELECT 1", cancellationToken);
            return new(true, "Connection succeeded.", stopwatch.Elapsed);
        }
        catch (Exception exception)
        {
            return new(false, exception.Message, stopwatch.Elapsed);
        }
    }

    public async Task<QueryResult> QueryAsync(DatabaseConnection connection, string sql, int maxRows, CancellationToken cancellationToken = default)
    {
        using var freeSql = Build(connection);
        var rawRows = await freeSql.Ado.QueryAsync<dynamic>(sql, cancellationToken);
        var materialized = new List<IReadOnlyDictionary<string, object?>>();

        foreach (var row in rawRows.Take(maxRows + 1))
        {
            materialized.Add(ToDictionary(row));
        }

        var truncated = materialized.Count > maxRows;
        if (truncated)
        {
            materialized.RemoveAt(materialized.Count - 1);
        }

        var columns = materialized.FirstOrDefault()?.Keys.ToArray() ?? [];
        return new(columns, materialized, truncated);
    }

    public async Task<int> ExecuteAsync(DatabaseConnection connection, string sql, CancellationToken cancellationToken = default)
    {
        using var freeSql = Build(connection);
        return await freeSql.Ado.ExecuteNonQueryAsync(sql, cancellationToken);
    }

    private IFreeSql Build(DatabaseConnection connection) => new FreeSqlBuilder()
        .UseConnectionString(ToDataType(Provider), BuildConnectionString(connection))
        .UseAutoSyncStructure(false)
        .Build();

    private string BuildConnectionString(DatabaseConnection connection) => Provider switch
    {
        DatabaseProvider.SqlServer => $"Server={connection.Host},{connection.Port};Database={connection.Database};User Id={connection.Username};Password={connection.Password};Encrypt=False;TrustServerCertificate=True;Connect Timeout=5;Command Timeout={connection.CommandTimeoutSeconds};",
        DatabaseProvider.MySql => $"Server={connection.Host};Port={connection.Port};Database={connection.Database};User ID={connection.Username};Password={connection.Password};Connection Timeout=5;Default Command Timeout={connection.CommandTimeoutSeconds};Allow User Variables=True;",
        DatabaseProvider.PostgreSql => $"Host={connection.Host};Port={connection.Port};Database={connection.Database};Username={connection.Username};Password={connection.Password};Timeout=5;Command Timeout={connection.CommandTimeoutSeconds};",
        DatabaseProvider.Sqlite => $"Data Source={connection.Database};",
        _ => throw new NotSupportedException($"Unsupported provider '{Provider}'.")
    };

    private static DataType ToDataType(DatabaseProvider provider) => provider switch
    {
        DatabaseProvider.SqlServer => DataType.SqlServer,
        DatabaseProvider.MySql => DataType.MySql,
        DatabaseProvider.PostgreSql => DataType.PostgreSQL,
        DatabaseProvider.Sqlite => DataType.Sqlite,
        _ => throw new NotSupportedException($"Unsupported provider '{provider}'.")
    };

    private static IReadOnlyDictionary<string, object?> ToDictionary(object row)
    {
        if (row is IReadOnlyDictionary<string, object?> readOnly)
        {
            return readOnly.ToDictionary(item => item.Key, item => Normalize(item.Value), StringComparer.OrdinalIgnoreCase);
        }

        if (row is IDictionary<string, object?> generic)
        {
            return generic.ToDictionary(item => item.Key, item => Normalize(item.Value), StringComparer.OrdinalIgnoreCase);
        }

        if (row is IDictionary dictionary)
        {
            return dictionary.Keys.Cast<object>().ToDictionary(
                key => Convert.ToString(key) ?? string.Empty,
                key => Normalize(dictionary[key]),
                StringComparer.OrdinalIgnoreCase);
        }

        return row.GetType().GetProperties().ToDictionary(
            property => property.Name,
            property => Normalize(property.GetValue(row)),
            StringComparer.OrdinalIgnoreCase);
    }

    private static object? Normalize(object? value) => value is DBNull ? null : value;
}
