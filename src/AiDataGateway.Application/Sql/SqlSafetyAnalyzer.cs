using System.Text.RegularExpressions;
using AiDataGateway.Domain.Sql;

namespace AiDataGateway.Application.Sql;

public sealed partial class SqlSafetyAnalyzer : ISqlSafetyAnalyzer
{
    private static readonly HashSet<string> ReadOperations = new(StringComparer.OrdinalIgnoreCase) { "SELECT", "WITH", "EXPLAIN", "SHOW", "DESCRIBE", "PRAGMA" };
    private static readonly HashSet<string> WriteOperations = new(StringComparer.OrdinalIgnoreCase) { "INSERT", "UPDATE", "DELETE", "MERGE", "REPLACE" };
    private static readonly HashSet<string> CriticalOperations = new(StringComparer.OrdinalIgnoreCase) { "DROP", "TRUNCATE", "ALTER", "CREATE", "GRANT", "REVOKE", "EXEC", "EXECUTE" };

    public SqlAnalysis Analyze(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return new(false, false, SqlRiskLevel.Critical, "UNKNOWN", ["SQL is required."]);
        }

        var cleaned = BlockCommentRegex().Replace(LineCommentRegex().Replace(sql, " "), " ").Trim();
        var statements = cleaned.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (statements.Length != 1)
        {
            return new(false, false, SqlRiskLevel.Critical, "MULTI_STATEMENT", ["Only one SQL statement is allowed per request."]);
        }

        var operation = FirstTokenRegex().Match(statements[0]).Value.ToUpperInvariant();
        if (ReadOperations.Contains(operation))
        {
            var reasons = new List<string>();
            if (SelectAllRegex().IsMatch(cleaned))
            {
                reasons.Add("SELECT * should be reviewed for unnecessary data exposure.");
            }

            return new(true, true, reasons.Count == 0 ? SqlRiskLevel.Low : SqlRiskLevel.Medium, operation, reasons);
        }

        if (WriteOperations.Contains(operation))
        {
            var reasons = new List<string> { "Write statements require local approval." };
            var risk = SqlRiskLevel.High;
            if ((operation is "UPDATE" or "DELETE") && !WhereRegex().IsMatch(cleaned))
            {
                risk = SqlRiskLevel.Critical;
                reasons.Add($"{operation} without a WHERE clause is blocked by default.");
            }

            return new(risk != SqlRiskLevel.Critical, false, risk, operation, reasons);
        }

        if (operation == "CREATE" && CreateTableRegex().IsMatch(cleaned))
        {
            return new(true, false, SqlRiskLevel.High, operation, ["CREATE TABLE requires local approval."]);
        }

        if (CriticalOperations.Contains(operation))
        {
            return new(false, false, SqlRiskLevel.Critical, operation, [$"{operation} is disabled by the default policy."]);
        }

        return new(false, false, SqlRiskLevel.Critical, string.IsNullOrWhiteSpace(operation) ? "UNKNOWN" : operation, ["The SQL operation is not recognized by the active policy."]);
    }

    [GeneratedRegex(@"--.*?(\r?\n|$)", RegexOptions.CultureInvariant)]
    private static partial Regex LineCommentRegex();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex BlockCommentRegex();

    [GeneratedRegex(@"^[A-Za-z]+", RegexOptions.CultureInvariant)]
    private static partial Regex FirstTokenRegex();

    [GeneratedRegex(@"\bWHERE\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WhereRegex();

    [GeneratedRegex(@"\bSELECT\s+\*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SelectAllRegex();

    [GeneratedRegex(@"^\s*CREATE\s+(?:(?:TEMP|TEMPORARY)\s+)?TABLE\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CreateTableRegex();
}
