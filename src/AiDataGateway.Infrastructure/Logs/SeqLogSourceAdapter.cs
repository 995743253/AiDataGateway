using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using AiDataGateway.Application.Abstractions;
using AiDataGateway.Domain.Logs;
using Microsoft.AspNetCore.Http;

namespace AiDataGateway.Infrastructure.Logs;

internal sealed class SeqLogSourceAdapter(IHttpClientFactory httpClientFactory) : ILogSourceAdapter
{
    public LogSourceType Type => LogSourceType.Seq;

    public async Task<LogSourceTestResult> TestAsync(LogSourceConnection connection, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await SendAsync(connection, 1, null, cancellationToken);
            return response.IsSuccessStatusCode
                ? new LogSourceTestResult(true, "Seq 连接成功，API Key 具备日志读取权限。")
                : new LogSourceTestResult(false, await ErrorAsync(response, cancellationToken));
        }
        catch (Exception exception)
        {
            return new LogSourceTestResult(false, exception.Message);
        }
    }

    public async Task<LogQueryResult> QueryAsync(LogSourceConnection connection, LogQueryOptions options, CancellationToken cancellationToken = default)
    {
        var requestedCount = Math.Clamp(options.Page * options.PageSize * 3, options.PageSize, 5_000);
        var filter = BuildFilter(options);
        using var response = await SendAsync(connection, requestedCount, filter, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(await ErrorAsync(response, cancellationToken));
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var elements = FindEventArray(document.RootElement).ToArray();
        var mapped = elements.Select(FromSeqEvent)
            .Where(item => !options.FromUtc.HasValue || !item.TimestampUtc.HasValue || item.TimestampUtc >= options.FromUtc)
            .Where(item => !options.ToUtc.HasValue || !item.TimestampUtc.HasValue || item.TimestampUtc <= options.ToUtc)
            .OrderByDescending(item => item.TimestampUtc ?? DateTimeOffset.MinValue)
            .ToArray();
        var skip = (options.Page - 1) * options.PageSize;
        var items = mapped.Skip(skip).Take(options.PageSize).ToArray();
        var partial = elements.Length >= requestedCount;
        return new LogQueryResult(items, options.Page, options.PageSize, mapped.Length, partial,
            partial ? $"Seq 本次返回达到 {requestedCount} 条上限，请缩小查询时间或增加过滤条件。" : null);
    }

    private async Task<HttpResponseMessage> SendAsync(
        LogSourceConnection connection,
        int count,
        string? filter,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(connection.Endpoint.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri))
        {
            throw new ArgumentException("Invalid Seq server address.");
        }

        var parameters = new List<KeyValuePair<string, string?>>
        {
            new("count", count.ToString(CultureInfo.InvariantCulture)),
            new("render", "true")
        };
        if (!string.IsNullOrWhiteSpace(filter)) parameters.Add(new("filter", filter));
        var uri = new Uri(baseUri, "api/events");
        var requestUri = uri.GetLeftPart(UriPartial.Path) + QueryString.Create(parameters);
        var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(connection.ApiKey)) request.Headers.TryAddWithoutValidation("X-Seq-ApiKey", connection.ApiKey);
        return await httpClientFactory.CreateClient("AiDataGateway.Seq").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    internal static string? BuildFilter(LogQueryOptions options)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(options.Query)) parts.Add($"({options.Query.Trim()})");
        if (!string.IsNullOrWhiteSpace(options.SearchText))
            parts.Add($"\"{options.SearchText.Trim().Replace("\\", "\\\\").Replace("\"", "\\\"")}\"");
        if (!string.IsNullOrWhiteSpace(options.PropertyName))
        {
            var name = options.PropertyName.Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_.]*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
                throw new ArgumentException("Seq 属性名只能包含字母、数字、下划线和点，且必须以字母或下划线开头。");
            parts.Add(string.IsNullOrWhiteSpace(options.PropertyValue)
                ? $"{name} is not null"
                : $"{name} = '{options.PropertyValue.Trim().Replace("'", "''")}'");
        }
        if (!string.IsNullOrWhiteSpace(options.Level)) parts.Add($"@Level = '{options.Level.Trim().Replace("'", "''")}'");
        if (options.FromUtc.HasValue) parts.Add($"@Timestamp >= DateTime('{options.FromUtc.Value.UtcDateTime:yyyy-MM-ddTHH:mm:ss.fff}Z')");
        if (options.ToUtc.HasValue) parts.Add($"@Timestamp <= DateTime('{options.ToUtc.Value.UtcDateTime:yyyy-MM-ddTHH:mm:ss.fff}Z')");
        return parts.Count == 0 ? null : string.Join(" and ", parts);
    }

    private static IEnumerable<JsonElement> FindEventArray(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array) return root.EnumerateArray().Select(item => item.Clone());
        if (root.ValueKind != JsonValueKind.Object) return [];
        foreach (var name in new[] { "Events", "events", "Items", "items" })
        {
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array)
            {
                return value.EnumerateArray().Select(item => item.Clone());
            }
        }
        return [];
    }

    private static StructuredLogEvent FromSeqEvent(JsonElement element)
    {
        var properties = element.ValueKind == JsonValueKind.Object
            ? element.EnumerateObject().ToDictionary(item => item.Name, item => JsonValue(item.Value), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, object?>();
        var raw = element.GetRawText();
        var id = GetString(properties, "Id", "id", "@i") ?? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)))[..24].ToLowerInvariant();
        var timestamp = ParseTimestamp(GetString(properties, "UtcTimestamp", "Timestamp", "@t"));
        var level = GetString(properties, "Level", "@l") ?? "Information";
        var message = GetString(properties, "RenderedMessage", "Message", "@m", "MessageTemplate", "@mt");
        var exception = GetString(properties, "Exception", "@x");
        if (properties.TryGetValue("Properties", out var nested) && nested is Dictionary<string, object?> nestedProperties)
        {
            foreach (var item in nestedProperties) properties.TryAdd(item.Key, item.Value);
        }
        return new StructuredLogEvent(id, timestamp, level, message, exception, properties, raw);
    }

    private static object? JsonValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
        JsonValueKind.Object => value.EnumerateObject().ToDictionary(item => item.Name, item => JsonValue(item.Value), StringComparer.OrdinalIgnoreCase),
        _ => JsonSerializer.Deserialize<object>(value.GetRawText())
    };

    private static string? GetString(IReadOnlyDictionary<string, object?> values, params string[] names)
    {
        foreach (var name in names)
        {
            if (values.TryGetValue(name, out var value)) return value?.ToString();
        }
        return null;
    }

    private static DateTimeOffset? ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp)
            ? timestamp.ToUniversalTime()
            : null;

    private static async Task<string> ErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (body.Length > 500) body = body[..500];
        return $"Seq request failed ({(int)response.StatusCode} {response.ReasonPhrase}). {body}".Trim();
    }
}
