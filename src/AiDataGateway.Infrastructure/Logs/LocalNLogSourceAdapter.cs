using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AiDataGateway.Application.Abstractions;
using AiDataGateway.Domain.Logs;

namespace AiDataGateway.Infrastructure.Logs;

public sealed partial class LocalNLogSourceAdapter : ILogSourceAdapter
{
    private const int MaximumReadBytes = 8 * 1024 * 1024;
    public LogSourceType Type => LogSourceType.LocalNLog;

    public Task<LogSourceTestResult> TestAsync(LogSourceConnection connection, CancellationToken cancellationToken = default)
    {
        try
        {
            var resolved = NLogConfigurationResolver.Resolve(connection);
            var files = NLogConfigurationResolver.FindFiles(resolved.FilePattern);
            return Task.FromResult(files.Count == 0
                ? new LogSourceTestResult(false, $"配置解析成功，但没有找到匹配的日志文件：{resolved.FilePattern}")
                : new LogSourceTestResult(true, $"已找到 {files.Count} 个日志文件，最新文件：{Path.GetFileName(files[0])}"));
        }
        catch (Exception exception)
        {
            return Task.FromResult(new LogSourceTestResult(false, exception.Message));
        }
    }

    public async Task<LogQueryResult> QueryAsync(LogSourceConnection connection, LogQueryOptions options, CancellationToken cancellationToken = default)
    {
        var resolved = NLogConfigurationResolver.Resolve(connection);
        var files = NLogConfigurationResolver.FindFiles(resolved.FilePattern, options.FromUtc, options.ToUtc);
        if (files.Count == 0)
        {
            throw new FileNotFoundException($"No log files match '{resolved.FilePattern}'.");
        }

        var remainingBytes = MaximumReadBytes;
        var chunks = new List<(string Text, bool Truncated, string File)>();
        foreach (var file in files)
        {
            if (remainingBytes <= 0)
            {
                break;
            }

            var chunk = await ReadTailAsync(file, remainingBytes, resolved.EncodingName, cancellationToken);
            remainingBytes -= chunk.BytesRead;
            chunks.Add((chunk.Text, chunk.Truncated, file));
        }

        var events = new List<StructuredLogEvent>();
        foreach (var chunk in chunks.AsEnumerable().Reverse())
        {
            var parsed = resolved.JsonLayout || LooksLikeJson(chunk.Text)
                ? ParseJsonDocuments(chunk.Text, chunk.Truncated, chunk.File)
                : ParseTextRecords(chunk.Text, resolved.Layout, chunk.Truncated, chunk.File);
            events.AddRange(parsed);
        }

        var filtered = events.Where(item => Matches(item, options))
            .OrderByDescending(item => item.TimestampUtc ?? DateTimeOffset.MinValue)
            .ToArray();
        var skip = (options.Page - 1) * options.PageSize;
        var items = filtered.Skip(skip).Take(options.PageSize).ToArray();
        var partial = chunks.Any(item => item.Truncated) || files.Count > chunks.Count;
        return new LogQueryResult(items, options.Page, options.PageSize, filtered.Length, partial,
            partial ? "日志文件较大，仅在所选日期文件的最近 8 MB 内容中查询；可缩小时间范围以查看更完整的结果。" : null);
    }

    internal static IReadOnlyList<StructuredLogEvent> ParseForTest(string text, string layout, bool json = false, bool truncated = false) =>
        json ? ParseJsonDocuments(text, truncated, "test.log") : ParseTextRecords(text, layout, truncated, "test.log");

    private static async Task<(string Text, int BytesRead, bool Truncated)> ReadTailAsync(string path, int maximumBytes, string? encodingName, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var bytesToRead = (int)Math.Min(stream.Length, maximumBytes);
        var truncated = stream.Length > bytesToRead;
        if (truncated)
        {
            stream.Seek(-bytesToRead, SeekOrigin.End);
        }

        var buffer = new byte[bytesToRead];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken);
            if (read == 0) break;
            total += read;
        }

        var encoding = await DetectEncodingAsync(path, encodingName, cancellationToken);
        var text = encoding.GetString(buffer, 0, total);
        if (truncated)
        {
            var firstNewLine = text.IndexOf('\n');
            if (firstNewLine >= 0)
            {
                text = text[(firstNewLine + 1)..];
            }
        }

        return (text, total, truncated);
    }

    private static async Task<Encoding> DetectEncodingAsync(string path, string? configuredName, CancellationToken cancellationToken)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        if (!string.IsNullOrWhiteSpace(configuredName))
        {
            try { return Encoding.GetEncoding(configuredName.Trim()); }
            catch (ArgumentException) { throw new ArgumentException($"不支持的日志编码：{configuredName}"); }
        }

        var prefix = new byte[Math.Min(64 * 1024, (int)Math.Min(new FileInfo(path).Length, int.MaxValue))];
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var count = await stream.ReadAsync(prefix, cancellationToken);
        if (count >= 3 && prefix[0] == 0xEF && prefix[1] == 0xBB && prefix[2] == 0xBF) return new UTF8Encoding(true);
        if (count >= 2 && prefix[0] == 0xFF && prefix[1] == 0xFE) return Encoding.Unicode;
        if (count >= 2 && prefix[0] == 0xFE && prefix[1] == 0xFF) return Encoding.BigEndianUnicode;
        try
        {
            _ = new UTF8Encoding(false, true).GetString(prefix, 0, count);
            return new UTF8Encoding(false);
        }
        catch (DecoderFallbackException)
        {
            // Most Windows NLog installations that do not use UTF-8 write with the local ANSI code page.
            // GB18030 also safely covers GBK/CP936 logs such as the supplied production sample.
            return CultureInfo.CurrentCulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                ? Encoding.GetEncoding("GB18030")
                : Encoding.Default;
        }
    }

    private static IReadOnlyList<StructuredLogEvent> ParseJsonDocuments(string text, bool truncated, string file)
    {
        var results = new List<StructuredLogEvent>();
        var index = 0;
        while (index < text.Length)
        {
            while (index < text.Length && (char.IsWhiteSpace(text[index]) || text[index] == ',')) index++;
            if (index >= text.Length) break;
            if (text[index] is not ('{' or '['))
            {
                var next = text.IndexOfAny(['{', '['], index + 1);
                var unparsedText = next < 0 ? text[index..] : text[index..next];
                if (!string.IsNullOrWhiteSpace(unparsedText)) results.Add(Unparsed(unparsedText, file, true, "JSON 记录前存在无法解析的内容。"));
                if (next < 0) break;
                index = next;
            }

            var start = index;
            var end = FindJsonEnd(text, start);
            if (end < 0)
            {
                results.Add(Unparsed(text[start..], file, true, "文件末尾 JSON 记录未闭合，已保留原始内容。"));
                break;
            }

            var raw = text[start..(end + 1)];
            try
            {
                using var document = JsonDocument.Parse(raw);
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in document.RootElement.EnumerateArray()) results.Add(FromJson(element, element.GetRawText(), file));
                }
                else
                {
                    results.Add(FromJson(document.RootElement, raw, file));
                }
            }
            catch (JsonException exception)
            {
                results.Add(Unparsed(raw, file, true, exception.Message));
            }

            index = end + 1;
        }

        if (truncated && results.Count > 0)
        {
            results[0] = results[0] with { Incomplete = true, ParseWarning = "日志文件只读取了尾部，第一条记录可能不完整。" };
        }
        return results;
    }

    private static int FindJsonEnd(string text, int start)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var index = start; index < text.Length; index++)
        {
            var current = text[index];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (current == '\\') escaped = true;
                else if (current == '"') inString = false;
                continue;
            }

            if (current == '"') inString = true;
            else if (current is '{' or '[') depth++;
            else if (current is '}' or ']')
            {
                depth--;
                if (depth == 0) return index;
            }
        }
        return -1;
    }

    private static StructuredLogEvent FromJson(JsonElement element, string raw, string file)
    {
        var properties = element.ValueKind == JsonValueKind.Object
            ? element.EnumerateObject().ToDictionary(item => item.Name, item => JsonValue(item.Value), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, object?> { ["value"] = JsonValue(element) };
        var timestamp = ParseTimestamp(GetString(properties, "@t", "Timestamp", "timestamp", "Time", "time"));
        var level = GetString(properties, "@l", "Level", "level") ?? "Information";
        var message = GetString(properties, "@m", "RenderedMessage", "Message", "message", "@mt");
        var exception = GetString(properties, "@x", "Exception", "exception");
        properties["_file"] = file;
        return new StructuredLogEvent(EventId(raw), timestamp, level, message, exception, properties, raw);
    }

    private static IReadOnlyList<StructuredLogEvent> ParseTextRecords(string text, string layout, bool truncated, string file)
    {
        var records = SplitRecords(text, layout);
        var parser = BuildLayoutParser(layout);
        var results = new List<StructuredLogEvent>(records.Count);
        for (var index = 0; index < records.Count; index++)
        {
            var raw = records[index];
            var fields = parser.Parse(raw);
            fields["_file"] = file;
            var timestamp = ParseTimestamp(GetString(fields, "timestamp", "longdate", "date", "time"));
            var level = GetString(fields, "level");
            var message = fields.ContainsKey("message") ? GetString(fields, "message") : raw;
            var exception = GetString(fields, "exception");
            var incomplete = truncated && index == 0;
            results.Add(new StructuredLogEvent(EventId(raw), timestamp, level, message, exception, fields, raw,
                incomplete, incomplete ? "日志文件只读取了尾部，第一条记录可能不完整。" : null));
        }
        return results;
    }

    private static IReadOnlyList<string> SplitRecords(string text, string layout)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var start = BuildRecordStartRegex(layout);
        var results = new List<string>();
        var current = new StringBuilder();
        foreach (var line in lines)
        {
            var begins = start.IsMatch(line);
            if (begins && current.Length > 0)
            {
                results.Add(current.ToString().TrimEnd('\r', '\n'));
                current.Clear();
            }

            if (current.Length > 0 || begins || !string.IsNullOrWhiteSpace(line)) current.AppendLine(line);
        }
        if (current.Length > 0) results.Add(current.ToString().TrimEnd('\r', '\n'));
        return results;
    }

    private static Regex BuildRecordStartRegex(string layout)
    {
        var prefix = layout.TrimStart();
        if (prefix.StartsWith("${longdate}", StringComparison.OrdinalIgnoreCase) || prefix.StartsWith("${date", StringComparison.OrdinalIgnoreCase))
        {
            return new Regex(@"^\d{4}[-/]\d{2}[-/]\d{2}[ T]", RegexOptions.CultureInvariant);
        }
        if (prefix.StartsWith("${level", StringComparison.OrdinalIgnoreCase))
        {
            return new Regex(@"^(Trace|Debug|Info|Information|Warn|Warning|Error|Fatal)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        return TimestampStartRegex();
    }

    private static LayoutParser BuildLayoutParser(string layout)
    {
        if (string.IsNullOrWhiteSpace(layout)) return new LayoutParser(null, []);
        var fields = new List<(string Group, string Name)>();
        var expression = new StringBuilder("\\A");
        var position = 0;
        var tokenIndex = 0;
        foreach (Match match in LayoutTokenRegex().Matches(layout))
        {
            expression.Append(Regex.Escape(layout[position..match.Index]));
            var token = match.Groups[1].Value;
            var name = NormalizeFieldName(token, tokenIndex);
            var group = $"f{tokenIndex++}";
            expression.Append($"(?<{group}>.*?)");
            fields.Add((group, name));
            position = match.Index + match.Length;
        }
        expression.Append(Regex.Escape(layout[position..])).Append("\\z");
        return new LayoutParser(new Regex(expression.ToString(), RegexOptions.Singleline | RegexOptions.CultureInvariant), fields);
    }

    private static string NormalizeFieldName(string token, int index)
    {
        var main = token.Split(':', 2)[0].Trim().ToLowerInvariant();
        if (main == "longdate") return "timestamp";
        if (main == "event-properties")
        {
            var item = Regex.Match(token, @"item=([^}:]+)", RegexOptions.IgnoreCase).Groups[1].Value;
            if (!string.IsNullOrWhiteSpace(item)) return item;
        }
        return string.IsNullOrWhiteSpace(main) ? $"field{index}" : main;
    }

    private static bool Matches(StructuredLogEvent item, LogQueryOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Level) && !string.Equals(item.Level, options.Level, StringComparison.OrdinalIgnoreCase)) return false;
        if (options.FromUtc.HasValue && item.TimestampUtc.HasValue && item.TimestampUtc < options.FromUtc) return false;
        if (options.ToUtc.HasValue && item.TimestampUtc.HasValue && item.TimestampUtc > options.ToUtc) return false;
        var text = string.IsNullOrWhiteSpace(options.SearchText) ? options.Query : options.SearchText;
        if (!string.IsNullOrWhiteSpace(text) &&
            !item.RawText.Contains(text, StringComparison.OrdinalIgnoreCase) &&
            !item.Properties.Any(property => property.Key.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                                             property.Value?.ToString()?.Contains(text, StringComparison.OrdinalIgnoreCase) == true)) return false;
        if (!string.IsNullOrWhiteSpace(options.PropertyName))
        {
            var property = item.Properties.FirstOrDefault(value => value.Key.Equals(options.PropertyName, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(property.Key)) return false;
            if (!string.IsNullOrWhiteSpace(options.PropertyValue) &&
                property.Value?.ToString()?.Contains(options.PropertyValue, StringComparison.OrdinalIgnoreCase) != true) return false;
        }
        return true;
    }

    private static StructuredLogEvent Unparsed(string raw, string file, bool incomplete, string warning) =>
        new(EventId(raw), null, null, raw.Trim(), null, new Dictionary<string, object?> { ["_file"] = file }, raw, incomplete, warning);

    private static string EventId(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..24].ToLowerInvariant();

    private static DateTimeOffset? ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var offset)) return offset.ToUniversalTime();
        return null;
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> fields, params string[] names)
    {
        foreach (var name in names)
        {
            if (fields.TryGetValue(name, out var value)) return value?.ToString();
        }
        return null;
    }

    private static object? JsonValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
        _ => JsonSerializer.Deserialize<object>(value.GetRawText())
    };

    private static bool LooksLikeJson(string text) => text.TrimStart().StartsWith('{') || text.TrimStart().StartsWith('[');

    private sealed record LayoutParser(Regex? Regex, IReadOnlyList<(string Group, string Name)> Fields)
    {
        public Dictionary<string, object?> Parse(string raw)
        {
            var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (Regex is null) return result;
            var match = Regex.Match(raw);
            if (!match.Success) return result;
            foreach (var field in Fields)
            {
                var value = match.Groups[field.Group].Value;
                result[field.Name] = value.Length == 0 ? null : value;
            }
            return result;
        }
    }

    [GeneratedRegex(@"\$\{([^{}]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex LayoutTokenRegex();

    [GeneratedRegex(@"^\s*(?:\[)?\d{4}[-/]\d{2}[-/]\d{2}[ T]", RegexOptions.CultureInvariant)]
    private static partial Regex TimestampStartRegex();
}
