namespace AiDataGateway.Application.Sql;

public interface ISqlTableAccessGuard
{
    IReadOnlyList<string> FindBlockedTables(string sql, IEnumerable<string> blockedTables);
}

public sealed class SqlTableAccessGuard : ISqlTableAccessGuard
{
    private static readonly HashSet<string> FromClauseBoundaries = new(StringComparer.OrdinalIgnoreCase)
    {
        "WHERE", "GROUP", "ORDER", "HAVING", "LIMIT", "OFFSET", "UNION", "EXCEPT", "INTERSECT", "RETURNING", "WINDOW"
    };

    private static readonly HashSet<string> TableModifiers = new(StringComparer.OrdinalIgnoreCase) { "LATERAL", "ONLY" };

    public IReadOnlyList<string> FindBlockedTables(string sql, IEnumerable<string> blockedTables)
    {
        var configured = blockedTables
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => new { Original = item.Trim(), Normalized = NormalizeIdentifier(item) })
            .Where(item => item.Normalized.Length > 0)
            .ToArray();
        if (configured.Length == 0 || string.IsNullOrWhiteSpace(sql))
        {
            return [];
        }

        var references = ExtractTableReferences(sql);
        return configured
            .Where(blocked => references.Any(reference => IsMatch(reference, blocked.Normalized)))
            .Select(blocked => blocked.Original)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsMatch(string reference, string blocked)
    {
        if (string.Equals(reference, blocked, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(LeafName(reference), LeafName(blocked), StringComparison.OrdinalIgnoreCase);
    }

    private static string LeafName(string identifier)
    {
        var separator = identifier.LastIndexOf('.');
        return separator < 0 ? identifier : identifier[(separator + 1)..];
    }

    private static IReadOnlyList<string> ExtractTableReferences(string sql)
    {
        var tokens = Tokenize(sql);
        var references = new List<string>();
        var fromContexts = new Dictionary<int, bool>();
        var depth = 0;

        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (token.Kind == SqlTokenKind.OpenParenthesis)
            {
                if (fromContexts.ContainsKey(depth) && fromContexts[depth])
                {
                    fromContexts[depth] = false;
                }
                depth++;
                continue;
            }
            if (token.Kind == SqlTokenKind.CloseParenthesis)
            {
                fromContexts.Remove(depth);
                depth = Math.Max(0, depth - 1);
                continue;
            }

            if (token.Kind == SqlTokenKind.Word && FromClauseBoundaries.Contains(token.Text))
            {
                fromContexts.Remove(depth);
                continue;
            }
            if (token.Kind == SqlTokenKind.Word && token.Text.Equals("FROM", StringComparison.OrdinalIgnoreCase))
            {
                fromContexts[depth] = true;
                continue;
            }
            if (token.Kind == SqlTokenKind.Word && token.Text.Equals("JOIN", StringComparison.OrdinalIgnoreCase) && fromContexts.ContainsKey(depth))
            {
                fromContexts[depth] = true;
                continue;
            }
            if (token.Kind == SqlTokenKind.Comma && fromContexts.ContainsKey(depth))
            {
                fromContexts[depth] = true;
                continue;
            }

            if (!fromContexts.TryGetValue(depth, out var expectsTable) || !expectsTable || token.Kind != SqlTokenKind.Word)
            {
                continue;
            }
            if (TableModifiers.Contains(token.Text))
            {
                continue;
            }

            var parts = new List<string> { token.Text };
            while (index + 2 < tokens.Count &&
                   tokens[index + 1].Kind == SqlTokenKind.Dot &&
                   tokens[index + 2].Kind == SqlTokenKind.Word)
            {
                parts.Add(tokens[index + 2].Text);
                index += 2;
            }
            references.Add(NormalizeIdentifier(string.Join('.', parts)));
            fromContexts[depth] = false;
        }

        return references;
    }

    private static List<SqlToken> Tokenize(string sql)
    {
        var tokens = new List<SqlToken>();
        for (var index = 0; index < sql.Length;)
        {
            var current = sql[index];
            if (char.IsWhiteSpace(current))
            {
                index++;
                continue;
            }
            if (current == '-' && index + 1 < sql.Length && sql[index + 1] == '-')
            {
                index += 2;
                while (index < sql.Length && sql[index] is not '\r' and not '\n') index++;
                continue;
            }
            if (current == '/' && index + 1 < sql.Length && sql[index + 1] == '*')
            {
                index += 2;
                while (index + 1 < sql.Length && !(sql[index] == '*' && sql[index + 1] == '/')) index++;
                index = Math.Min(sql.Length, index + 2);
                continue;
            }
            if (current == '\'')
            {
                index++;
                while (index < sql.Length)
                {
                    if (sql[index] != '\'') { index++; continue; }
                    if (index + 1 < sql.Length && sql[index + 1] == '\'') { index += 2; continue; }
                    index++;
                    break;
                }
                continue;
            }
            if (current is '"' or '`' or '[')
            {
                var closing = current == '[' ? ']' : current;
                index++;
                var start = index;
                var value = new System.Text.StringBuilder();
                while (index < sql.Length)
                {
                    if (sql[index] != closing)
                    {
                        value.Append(sql[index++]);
                        continue;
                    }
                    if (index + 1 < sql.Length && sql[index + 1] == closing)
                    {
                        value.Append(closing);
                        index += 2;
                        continue;
                    }
                    index++;
                    break;
                }
                tokens.Add(new SqlToken(SqlTokenKind.Word, value.Length > 0 ? value.ToString() : sql[start..Math.Min(index, sql.Length)].Trim(closing)));
                continue;
            }
            if (char.IsLetterOrDigit(current) || current is '_' or '$' or '#' or '@')
            {
                var start = index++;
                while (index < sql.Length && (char.IsLetterOrDigit(sql[index]) || sql[index] is '_' or '$' or '#' or '@')) index++;
                tokens.Add(new SqlToken(SqlTokenKind.Word, sql[start..index]));
                continue;
            }

            tokens.Add(current switch
            {
                '(' => new SqlToken(SqlTokenKind.OpenParenthesis, "("),
                ')' => new SqlToken(SqlTokenKind.CloseParenthesis, ")"),
                ',' => new SqlToken(SqlTokenKind.Comma, ","),
                '.' => new SqlToken(SqlTokenKind.Dot, "."),
                _ => new SqlToken(SqlTokenKind.Other, current.ToString())
            });
            index++;
        }
        return tokens;
    }

    private static string NormalizeIdentifier(string identifier) => string.Join('.', identifier
        .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(part => part.Trim().Trim('"', '`', '[', ']')))
        .ToLowerInvariant();

    private readonly record struct SqlToken(SqlTokenKind Kind, string Text);

    private enum SqlTokenKind
    {
        Word,
        OpenParenthesis,
        CloseParenthesis,
        Comma,
        Dot,
        Other
    }
}
