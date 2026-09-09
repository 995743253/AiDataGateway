using System.Text.RegularExpressions;
using AiDataGateway.Application.Abstractions;

namespace AiDataGateway.Application.Logs;

/// <summary>
/// 从日志事件中提取内嵌 SQL：优先命中约定名称的结构化属性（sql/query/commandText 等），
/// 否则按语句关键字从属性值、消息、原始文本中截取，供前端 SQL 追踪开窗查询使用。
/// </summary>
public static partial class LogSqlTextExtractor
{
    private static readonly string[] PriorityKeys =
        ["sql", "query", "commandtext", "command_text", "statement", "databasecommand", "database_command"];

    private static readonly string[] StatementWords =
        ["select", "insert", "update", "delete", "create", "alter", "drop", "truncate", "merge", "with"];

    private static readonly string[] SupportWords =
        ["from", "into", "set", "table", "values", "database", "index", "view", "procedure"];

    [GeneratedRegex(@"[a-z_]+", RegexOptions.IgnoreCase)]
    private static partial Regex WordRegex();

    [GeneratedRegex(@"\b(select|insert|update|delete|create|alter|drop|truncate|merge|with)\b[\s\S]*", RegexOptions.IgnoreCase)]
    private static partial Regex SqlCandidateRegex();

    public static string? Extract(StructuredLogEvent logEvent)
    {
        foreach (var (key, value) in logEvent.Properties)
        {
            if (!PriorityKeys.Contains(NormalizeKey(key))) continue;
            if (value is string text && LooksLikeSql(text)) return text.Trim();
        }

        foreach (var value in logEvent.Properties.Values)
        {
            if (value is not string text) continue;
            var extracted = MatchSql(text);
            if (extracted is not null) return extracted;
        }

        return MatchSql(logEvent.Message) ?? MatchSql(logEvent.RawText);
    }

    private static string? MatchSql(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var match = SqlCandidateRegex().Match(text);
        if (!match.Success || !LooksLikeSql(match.Value)) return null;
        return match.Value.Trim();
    }

    private static string NormalizeKey(string key) => key.Replace("-", "_").Replace(" ", "_").ToLowerInvariant();

    private static bool LooksLikeSql(string text)
    {
        var words = WordRegex().Matches(text.ToLowerInvariant());
        if (words.Count == 0) return false;
        if (!StatementWords.Contains(words[0].Value)) return false;
        for (var index = 1; index < words.Count; index++)
        {
            if (SupportWords.Contains(words[index].Value)) return true;
        }

        return false;
    }
}
