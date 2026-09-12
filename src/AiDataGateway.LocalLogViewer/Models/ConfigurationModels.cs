using System;
using System.Collections.Generic;
using System.Data;

namespace AiDataGateway.LocalLogViewer.Models
{
    public sealed class LogProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "新日志配置";
        public string RootDirectory { get; set; } = string.Empty;
        public string NLogConfigurationPath { get; set; } = string.Empty;
        public string NLogTargetName { get; set; } = string.Empty;
        public string LayoutOverride { get; set; } = string.Empty;
        public string EncodingName { get; set; } = string.Empty;
        public string FilePattern { get; set; } = "*.log";
        public int MaximumReadMegabytesPerFile { get; set; } = 10;
        public string DatabaseProfileId { get; set; } = string.Empty;
        public bool IsActive { get; set; }

        public LogProfile Copy() => (LogProfile)MemberwiseClone();
        public override string ToString() => Name;
    }

    public sealed class DatabaseProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "新数据库配置";
        public string ProviderInvariantName { get; set; } = "System.Data.SqlClient";
        public string Host { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 1433;
        public string Database { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string ProtectedPassword { get; set; } = string.Empty;
        public bool IntegratedSecurity { get; set; }
        public int ConnectionTimeoutSeconds { get; set; } = 8;

        public bool HasProtectedPassword => !string.IsNullOrWhiteSpace(ProtectedPassword);
        public DatabaseProfile Copy() => (DatabaseProfile)MemberwiseClone();
        public override string ToString() => Name;
    }

    public sealed class ProviderOption
    {
        public ProviderOption(string name, string invariantName, int port)
        {
            Name = name;
            InvariantName = invariantName;
            DefaultPort = port;
        }

        public string Name { get; }
        public string InvariantName { get; }
        public int DefaultPort { get; }
        public override string ToString() => Name;
    }

    public sealed class LogEntry
    {
        public string Id { get; set; }
        public DateTime? Timestamp { get; set; }
        public string Level { get; set; }
        public string Message { get; set; }
        public string Exception { get; set; }
        public string FilePath { get; set; }
        public string RawText { get; set; }
        public bool Incomplete { get; set; }
        public string SqlText { get; set; }
        public string FormattedSql { get; set; }
        public bool HasSql => !string.IsNullOrWhiteSpace(SqlText);
        public bool SqlIsReadOnly { get; set; }
        public string FunctionName { get; set; }
        public IDictionary<string, object> Properties { get; set; } = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        public string DisplayTime => Timestamp.HasValue ? Timestamp.Value.ToString("yyyy-MM-dd HH:mm:ss.fff") : "—";
        public string FileName => System.IO.Path.GetFileName(FilePath ?? string.Empty);
    }

    public sealed class StructuredNode
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public IList<StructuredNode> Children { get; set; } = new List<StructuredNode>();
        public bool HasChildren => Children.Count > 0;
    }

    public sealed class LogReadResult
    {
        public IList<LogEntry> Items { get; set; } = new List<LogEntry>();
        public int FilesScanned { get; set; }
        public bool Truncated { get; set; }
        public string Warning { get; set; }
        public DateTime? LatestTimestamp { get; set; }
        public DateTime? RangeStart { get; set; }
        public DateTime? RangeEnd { get; set; }
    }

    public sealed class RecentRangeOption
    {
        public RecentRangeOption(string name, TimeSpan? duration) { Name = name; Duration = duration; }
        public string Name { get; }
        public TimeSpan? Duration { get; }
        public override string ToString() => Name;
    }

    public sealed class SqlQueryResult
    {
        public DataTable Table { get; set; }
        public bool Truncated { get; set; }
    }

    public sealed class SqlPlaceholder
    {
        public string Key { get; set; }
        public string Token { get; set; }
        public string Label { get; set; }
    }

    public sealed class SqlBoundParameter
    {
        public string Name { get; set; }
        public object Value { get; set; }
    }

    public sealed class PreparedSqlQuery
    {
        public string Sql { get; set; }
        public IList<SqlBoundParameter> Parameters { get; set; } = new List<SqlBoundParameter>();
    }
}
