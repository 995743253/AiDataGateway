namespace AiDataGateway.Domain.DataSources;

public sealed class DataSourceDefinition
{
    private DataSourceDefinition()
    {
    }

    public DataSourceDefinition(
        string key,
        string name,
        DatabaseProvider provider,
        string host,
        int port,
        string database,
        string username,
        DataSourceAccessMode accessMode)
    {
        Id = Guid.NewGuid();
        Update(key, name, provider, host, port, database, username, accessMode, 1_000, 30, true);
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public DatabaseProvider Provider { get; private set; }
    public string Host { get; private set; } = string.Empty;
    public int Port { get; private set; }
    public string Database { get; private set; } = string.Empty;
    public string Username { get; private set; } = string.Empty;
    public string ProtectedPassword { get; private set; } = string.Empty;
    public DataSourceAccessMode AccessMode { get; private set; }
    public int MaxRows { get; private set; }
    public int CommandTimeoutSeconds { get; private set; }
    public bool Enabled { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void Update(
        string key,
        string name,
        DatabaseProvider provider,
        string host,
        int port,
        string database,
        string username,
        DataSourceAccessMode accessMode,
        int maxRows,
        int commandTimeoutSeconds,
        bool enabled)
    {
        Key = Require(key, nameof(key)).ToLowerInvariant();
        Name = Require(name, nameof(name));
        Provider = provider;
        Host = Require(host, nameof(host));
        Port = port is > 0 and <= 65_535 ? port : throw new ArgumentOutOfRangeException(nameof(port));
        Database = Require(database, nameof(database));
        Username = Require(username, nameof(username));
        AccessMode = accessMode;
        MaxRows = Math.Clamp(maxRows, 1, 10_000);
        CommandTimeoutSeconds = Math.Clamp(commandTimeoutSeconds, 1, 300);
        Enabled = enabled;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void SetProtectedPassword(string protectedPassword)
    {
        ProtectedPassword = Require(protectedPassword, nameof(protectedPassword));
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static string Require(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }

        return value.Trim();
    }
}
