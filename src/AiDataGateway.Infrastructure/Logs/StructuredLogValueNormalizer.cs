using System.Text.Json;

namespace AiDataGateway.Infrastructure.Logs;

internal static class StructuredLogValueNormalizer
{
    private const int MaximumDepth = 10;
    private const int MaximumJsonStringLength = 2 * 1024 * 1024;

    public static object? FromJsonElement(JsonElement value, int depth = 0)
    {
        if (depth >= MaximumDepth) return value.GetRawText();
        return value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => FromString(value.GetString(), depth + 1),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
            JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.Object => value.EnumerateObject().ToDictionary(
                item => item.Name,
                item => FromJsonElement(item.Value, depth + 1),
                StringComparer.OrdinalIgnoreCase),
            JsonValueKind.Array => value.EnumerateArray().Select(item => FromJsonElement(item, depth + 1)).ToArray(),
            _ => value.GetRawText()
        };
    }

    public static object? FromString(string? value, int depth = 0)
    {
        if (value is null || depth >= MaximumDepth || value.Length > MaximumJsonStringLength) return value;
        var trimmed = value.Trim();
        if (trimmed.Length < 2 || (trimmed[0], trimmed[^1]) is not (('{', '}') or ('[', ']'))) return value;
        try
        {
            using var document = JsonDocument.Parse(trimmed);
            return FromJsonElement(document.RootElement, depth + 1);
        }
        catch (JsonException)
        {
            return value;
        }
    }

    public static bool TryParseString(string? value, out object? parsed)
    {
        parsed = FromString(value);
        return value is not null && !ReferenceEquals(parsed, value) && parsed is not string;
    }

    public static object? FindProperty(object? value, string propertyName, int depth = 0)
    {
        if (value is null || depth >= MaximumDepth) return null;
        if (value is IReadOnlyDictionary<string, object?> readOnly)
        {
            foreach (var item in readOnly)
            {
                if (item.Key.Equals(propertyName, StringComparison.OrdinalIgnoreCase)) return item.Value;
            }
            foreach (var item in readOnly.Values)
            {
                var nested = FindProperty(item, propertyName, depth + 1);
                if (nested is not null) return nested;
            }
        }
        else if (value is IEnumerable<object?> list)
        {
            foreach (var item in list)
            {
                var nested = FindProperty(item, propertyName, depth + 1);
                if (nested is not null) return nested;
            }
        }
        return null;
    }
}
