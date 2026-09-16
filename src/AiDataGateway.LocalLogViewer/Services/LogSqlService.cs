using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AiDataGateway.LocalLogViewer.Models;

namespace AiDataGateway.LocalLogViewer.Services
{
    public sealed class LogSqlService
    {
        private static readonly string[] PriorityKeys = { "sql", "query", "commandtext", "command_text", "statement", "databasecommand", "database_command" };
        private static readonly Regex SqlCandidateRegex = new Regex(@"\b(select|insert|update|delete|create|alter|drop|truncate|merge|with)\b[\s\S]*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SqlWordRegex = new Regex(@"[a-z_]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex DangerousWordRegex = new Regex(@"\b(insert|update|delete|create|alter|drop|truncate|merge|replace|upsert|execute|exec|call|grant|revoke|attach|detach|pragma|vacuum|reindex)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SelectIntoRegex = new Regex(@"\bselect\b[\s\S]*\binto\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex TokenRegex = new Regex(@"('(?:''|[^'])*'|""(?:""""|[^""])*""|--[^\r\n]*|/\*[\s\S]*?\*/|(?<!@)@[A-Za-z_][A-Za-z0-9_]*|(?<!:):[A-Za-z_][A-Za-z0-9_]*|\$\d+|\?|>=|<=|<>|!=|:=|::|->>|->|\|\||&&|\b[A-Za-z_][A-Za-z0-9_]*\b|[(),;.]|\s+|.)", RegexOptions.Compiled);
        private static readonly Regex PlaceholderRegex = new Regex(@"(?<!@)@[A-Za-z_][A-Za-z0-9_]*|(?<!:):[A-Za-z_][A-Za-z0-9_]*|\$\d+|\?", RegexOptions.Compiled);
        private static readonly HashSet<string> StatementWords = new HashSet<string>(new[] { "select", "insert", "update", "delete", "create", "alter", "drop", "truncate", "merge", "with" }, StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> SupportWords = new HashSet<string>(new[] { "from", "into", "set", "table", "values", "database", "index", "view", "procedure", "join", "where", "as" }, StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> NewLineKeywords = new HashSet<string>(new[] { "SELECT", "FROM", "WHERE", "GROUP", "ORDER", "HAVING", "UNION", "LIMIT", "OFFSET", "FETCH", "JOIN", "LEFT", "RIGHT", "INNER", "OUTER", "CROSS", "VALUES", "SET", "RETURNING" }, StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> JoinModifiers = new HashSet<string>(new[] { "LEFT", "RIGHT", "INNER", "OUTER", "FULL", "CROSS" }, StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> UpperKeywords = new HashSet<string>(new[] { "SELECT", "FROM", "WHERE", "AS", "AND", "OR", "NOT", "NULL", "IS", "IN", "EXISTS", "LIKE", "BETWEEN", "CASE", "WHEN", "THEN", "ELSE", "END", "DISTINCT", "TOP", "GROUP", "BY", "ORDER", "ASC", "DESC", "HAVING", "UNION", "ALL", "LIMIT", "OFFSET", "FETCH", "NEXT", "ROWS", "ONLY", "JOIN", "LEFT", "RIGHT", "INNER", "OUTER", "FULL", "CROSS", "ON", "WITH", "VALUES", "SET", "RETURNING" }, StringComparer.OrdinalIgnoreCase);

        public void Populate(LogEntry entry)
        {
            if (entry == null) return;
            var sql = Extract(entry);
            entry.SqlText = sql;
            entry.FormattedSql = string.IsNullOrWhiteSpace(sql) ? string.Empty : Format(sql);
            entry.SqlIsReadOnly = IsReadOnly(sql);
        }

        public string Extract(LogEntry entry)
        {
            foreach (var pair in entry.Properties)
            {
                if (!PriorityKeys.Contains(NormalizeKey(pair.Key))) continue;
                foreach (var text in StringValues(pair.Value)) if (LooksLikeSql(text)) return text.Trim();
            }
            foreach (var value in entry.Properties.Values)
                foreach (var text in StringValues(value)) { var sql = MatchSql(text); if (sql != null) return sql; }
            return MatchSql(entry.Message) ?? MatchSql(entry.RawText);
        }

        public bool IsReadOnly(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql)) return false;
            var sanitized = StripCommentsAndQuotedText(sql).Trim();
            while (sanitized.EndsWith(";", StringComparison.Ordinal)) sanitized = sanitized.Substring(0, sanitized.Length - 1).TrimEnd();
            if (sanitized.IndexOf(';') >= 0) return false;
            if (!Regex.IsMatch(sanitized, @"^(select|with)\b", RegexOptions.IgnoreCase)) return false;
            if (DangerousWordRegex.IsMatch(sanitized) || SelectIntoRegex.IsMatch(sanitized)) return false;
            return true;
        }

        public int CountPlaceholders(string sql)
        {
            return GetPlaceholders(sql).Count;
        }

        public IList<SqlPlaceholder> GetPlaceholders(string sql)
        {
            var result = new List<SqlPlaceholder>();
            var named = new Dictionary<string, SqlPlaceholder>(StringComparer.OrdinalIgnoreCase);
            var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var anonymousIndex = 0;
            foreach (var occurrence in PlaceholderOccurrences(sql))
            {
                if (occurrence.Token == "?")
                {
                    anonymousIndex++;
                    var anonymousKey = UniquePlaceholderKey("p" + anonymousIndex, usedKeys);
                    result.Add(new SqlPlaceholder { Key = anonymousKey, Token = occurrence.Token, Label = "?（第 " + anonymousIndex + " 个）" });
                    continue;
                }
                var identity = occurrence.Token;
                SqlPlaceholder existing;
                if (named.TryGetValue(identity, out existing)) continue;
                var key = UniquePlaceholderKey(occurrence.Token.TrimStart('@', ':', '$'), usedKeys);
                existing = new SqlPlaceholder { Key = key, Token = occurrence.Token, Label = occurrence.Token };
                named[identity] = existing;
                result.Add(existing);
            }
            return result;
        }

        public PreparedSqlQuery Prepare(string sql, IList<object> values, string providerInvariantName)
        {
            var placeholders = GetPlaceholders(sql);
            if (values == null) values = new List<object>();
            if (placeholders.Count != values.Count) throw new ArgumentException("SQL 包含 " + placeholders.Count + " 个参数，但提供了 " + values.Count + " 个值。");

            var markerPrefix = (providerInvariantName ?? string.Empty).IndexOf("Oracle", StringComparison.OrdinalIgnoreCase) >= 0 ? ":" : "@";
            var namedIndexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var logicalIndex = 0;
            var sourceIndex = 0;
            var builder = new StringBuilder();
            foreach (var occurrence in PlaceholderOccurrences(sql))
            {
                builder.Append(sql, sourceIndex, occurrence.Index - sourceIndex);
                int valueIndex;
                if (occurrence.Token == "?") valueIndex = logicalIndex++;
                else if (!namedIndexes.TryGetValue(occurrence.Token, out valueIndex)) { valueIndex = logicalIndex++; namedIndexes[occurrence.Token] = valueIndex; }
                builder.Append(markerPrefix).Append("p").Append(valueIndex);
                sourceIndex = occurrence.Index + occurrence.Length;
            }
            builder.Append(sql, sourceIndex, sql.Length - sourceIndex);

            var prepared = new PreparedSqlQuery { Sql = builder.ToString() };
            for (var index = 0; index < values.Count; index++)
                prepared.Parameters.Add(new SqlBoundParameter { Name = markerPrefix + "p" + index, Value = values[index] ?? DBNull.Value });
            return prepared;
        }

        private static string UniquePlaceholderKey(string preferred, ISet<string> usedKeys)
        {
            var key = string.IsNullOrWhiteSpace(preferred) ? "parameter" : preferred;
            if (usedKeys.Add(key)) return key;
            var suffix = 2;
            while (!usedKeys.Add(key + "_" + suffix)) suffix++;
            return key + "_" + suffix;
        }

        public string Substitute(string sql, IList<string> parameters)
        {
            var expected = CountPlaceholders(sql);
            if (expected != parameters.Count) throw new ArgumentException("SQL 包含 " + expected + " 个 ? 占位符，但提供了 " + parameters.Count + " 个参数。");
            var builder = new StringBuilder(); var parameterIndex = 0; var inString = false;
            for (var index = 0; index < sql.Length; index++)
            {
                var current = sql[index];
                if (current == '\'')
                {
                    if (inString && index + 1 < sql.Length && sql[index + 1] == '\'') { builder.Append("''"); index++; }
                    else { inString = !inString; builder.Append(current); }
                }
                else if (!inString && current == '?') builder.Append(FormatLiteral(parameters[parameterIndex++]));
                else builder.Append(current);
            }
            return builder.ToString();
        }

        public string Format(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql)) return string.Empty;
            var builder = new StringBuilder(); var indent = 0; var lineStart = true; var previousWord = string.Empty;
            foreach (Match match in TokenRegex.Matches(sql.Trim()))
            {
                var token = match.Value;
                if (string.IsNullOrWhiteSpace(token)) continue;
                if (token == ")") indent = Math.Max(0, indent - 1);
                var keyword = UpperKeywords.Contains(token) ? token.ToUpperInvariant() : token;
                if (NewLineKeywords.Contains(keyword) && !(string.Equals(keyword, "JOIN", StringComparison.OrdinalIgnoreCase) && JoinModifiers.Contains(previousWord)) && builder.Length > 0 && !lineStart) { builder.AppendLine(); lineStart = true; }
                if (lineStart) { builder.Append(new string(' ', indent * 2)); lineStart = false; }
                if (token == ",") { builder.Append(',').AppendLine(); lineStart = true; continue; }
                if (token == "(") { if (NeedsSpace(builder)) builder.Append(' '); builder.Append('('); indent++; continue; }
                if (token == ")") { builder.Append(')'); continue; }
                if (token == ".") { builder.Append('.'); continue; }
                if (token == ";") { builder.Append(';').AppendLine(); lineStart = true; continue; }
                if (NeedsSpace(builder)) builder.Append(' ');
                builder.Append(keyword);
                if (char.IsLetter(token[0])) previousWord = keyword;
            }
            return builder.ToString().Trim();
        }

        private static IEnumerable<string> StringValues(object value, int depth = 0)
        {
            if (value == null || depth > 10) yield break;
            var text = value as string;
            if (text != null) { yield return text; yield break; }
            var dictionary = value as IDictionary<string, object>;
            if (dictionary != null) { foreach (var item in dictionary.Values) foreach (var nested in StringValues(item, depth + 1)) yield return nested; yield break; }
            var enumerable = value as IEnumerable;
            if (enumerable != null) foreach (var item in enumerable) foreach (var nested in StringValues(item, depth + 1)) yield return nested;
        }

        private static string MatchSql(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var match = SqlCandidateRegex.Match(text);
            return match.Success && LooksLikeSql(match.Value) ? match.Value.Trim() : null;
        }
        private static bool LooksLikeSql(string text)
        {
            var words = SqlWordRegex.Matches(text).Cast<Match>().Select(match => match.Value).ToArray();
            return words.Length > 1 && StatementWords.Contains(words[0]) && words.Skip(1).Any(SupportWords.Contains);
        }
        private static string NormalizeKey(string key) { return (key ?? string.Empty).Replace("-", "_").Replace(" ", "_").ToLowerInvariant(); }
        private static bool NeedsSpace(StringBuilder builder) { return builder.Length > 0 && !char.IsWhiteSpace(builder[builder.Length - 1]) && builder[builder.Length - 1] != '(' && builder[builder.Length - 1] != '.'; }
        private static string FormatLiteral(string value)
        {
            if (value == null || string.Equals(value.Trim(), "null", StringComparison.OrdinalIgnoreCase)) return "NULL";
            var trimmed = value.Trim();
            if (Regex.IsMatch(trimmed, @"^[+-]?\d+(\.\d+)?([eE][+-]?\d+)?$")) return trimmed;
            if (string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase)) return "1";
            if (string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase)) return "0";
            return "'" + value.Replace("'", "''") + "'";
        }
        private static string StripCommentsAndQuotedText(string sql)
        {
            var result = new StringBuilder(sql.Length); var mode = 0;
            for (var index = 0; index < sql.Length; index++)
            {
                var current = sql[index]; var next = index + 1 < sql.Length ? sql[index + 1] : '\0';
                if (mode == 0)
                {
                    if (current == '\'') { mode = 1; result.Append(' '); }
                    else if (current == '"') { mode = 2; result.Append(' '); }
                    else if (current == '-' && next == '-') { mode = 3; result.Append("  "); index++; }
                    else if (current == '/' && next == '*') { mode = 4; result.Append("  "); index++; }
                    else result.Append(current);
                }
                else if (mode == 1 && current == '\'') { if (next == '\'') { result.Append("  "); index++; } else { mode = 0; result.Append(' '); } }
                else if (mode == 2 && current == '"') { if (next == '"') { result.Append("  "); index++; } else { mode = 0; result.Append(' '); } }
                else if (mode == 3 && (current == '\r' || current == '\n')) { mode = 0; result.Append(current); }
                else if (mode == 4 && current == '*' && next == '/') { mode = 0; result.Append("  "); index++; }
                else result.Append(char.IsWhiteSpace(current) ? current : ' ');
            }
            return result.ToString();
        }

        private static IEnumerable<PlaceholderOccurrence> PlaceholderOccurrences(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql)) yield break;
            var searchable = StripCommentsAndQuotedText(sql);
            foreach (Match match in PlaceholderRegex.Matches(searchable))
                yield return new PlaceholderOccurrence(match.Index, match.Length, sql.Substring(match.Index, match.Length));
        }

        private sealed class PlaceholderOccurrence
        {
            public PlaceholderOccurrence(int index, int length, string token) { Index = index; Length = length; Token = token; }
            public int Index { get; private set; }
            public int Length { get; private set; }
            public string Token { get; private set; }
        }
    }
}
