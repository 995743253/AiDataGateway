using System.Text;
using System.Text.RegularExpressions;

namespace AiDataGateway.Application.Sql;

/// <summary>
/// 处理日志 SQL 追踪中带 <c>?</c> 占位符的参数化查询：按位置把参数替换为安全的 SQL 字面量。
/// 占位符统计与替换都会跳过单引号字符串内部；替换后的完整 SQL 仍会经过只读策略校验。
/// </summary>
public static partial class LogSqlParameterSubstitutor
{
    [GeneratedRegex(@"^[+-]?\d+(\.\d+)?([eE][+-]?\d+)?$")]
    private static partial Regex NumberRegex();

    public static int CountPlaceholders(string sql)
    {
        var count = 0;
        var inString = false;
        for (var index = 0; index < sql.Length; index++)
        {
            var current = sql[index];
            if (current == '\'')
            {
                if (inString && index + 1 < sql.Length && sql[index + 1] == '\'') index++;
                else inString = !inString;
                continue;
            }

            if (!inString && current == '?') count++;
        }

        return count;
    }

    public static string Substitute(string sql, IReadOnlyList<string?> parameters)
    {
        var builder = new StringBuilder(sql.Length + parameters.Count * 16);
        var parameterIndex = 0;
        var inString = false;
        for (var index = 0; index < sql.Length; index++)
        {
            var current = sql[index];
            if (current == '\'')
            {
                if (inString && index + 1 < sql.Length && sql[index + 1] == '\'')
                {
                    builder.Append("''");
                    index++;
                }
                else
                {
                    inString = !inString;
                    builder.Append('\'');
                }

                continue;
            }

            if (!inString && current == '?')
            {
                if (parameterIndex >= parameters.Count)
                {
                    throw new ArgumentException($"SQL 包含 {CountPlaceholders(sql)} 个 \"?\" 占位符，但只提供了 {parameters.Count} 个参数。");
                }

                builder.Append(FormatLiteral(parameters[parameterIndex]));
                parameterIndex++;
            }
            else
            {
                builder.Append(current);
            }
        }

        if (parameterIndex != parameters.Count)
        {
            throw new ArgumentException($"SQL 包含 {parameterIndex} 个 \"?\" 占位符，但提供了 {parameters.Count} 个参数。");
        }

        return builder.ToString();
    }

    private static string FormatLiteral(string? value)
    {
        if (value is null) return "NULL";
        var trimmed = value.Trim();
        if (trimmed.Length == 0) return "''";
        if (string.Equals(trimmed, "null", StringComparison.OrdinalIgnoreCase)) return "NULL";
        if (string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase)) return "1";
        if (string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase)) return "0";
        if (NumberRegex().IsMatch(trimmed)) return trimmed;
        return "'" + value.Replace("'", "''") + "'";
    }
}
