using System.Text.RegularExpressions;
using System.Xml.Linq;
using AiDataGateway.Application.Abstractions;

namespace AiDataGateway.Infrastructure.Logs;

internal sealed record ResolvedNLogConfiguration(string FilePattern, string Layout, bool JsonLayout, string BaseDirectory, string? EncodingName);

internal static partial class NLogConfigurationResolver
{
    public static ResolvedNLogConfiguration Resolve(LogSourceConnection connection)
    {
        var configText = connection.NLogConfiguration.Trim();
        var configDirectory = AppContext.BaseDirectory;
        if (!string.IsNullOrWhiteSpace(configText) && !configText.StartsWith('<'))
        {
            var configPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(configText));
            if (!File.Exists(configPath))
            {
                throw new FileNotFoundException("NLog configuration file was not found.", configPath);
            }

            configDirectory = Path.GetDirectoryName(configPath) ?? AppContext.BaseDirectory;
            configText = File.ReadAllText(configPath);
        }

        string? configuredFile = null;
        string? configuredLayout = null;
        string? configuredEncoding = null;
        var jsonLayout = false;
        if (!string.IsNullOrWhiteSpace(configText))
        {
            var document = XDocument.Parse(configText, LoadOptions.PreserveWhitespace);
            var variables = document.Descendants()
                .Where(item => item.Name.LocalName.Equals("variable", StringComparison.OrdinalIgnoreCase))
                .Select(item => new { Name = Attribute(item, "name"), Value = Attribute(item, "value") })
                .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                .ToDictionary(item => item.Name!, item => item.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase);
            var targets = document.Descendants().Where(item => item.Name.LocalName.Equals("target", StringComparison.OrdinalIgnoreCase));
            var target = targets.FirstOrDefault(item =>
            {
                var type = Attribute(item, "type") ?? Attribute(item, "xsi:type") ?? string.Empty;
                var name = Attribute(item, "name") ?? string.Empty;
                return type.Contains("File", StringComparison.OrdinalIgnoreCase) &&
                       (string.IsNullOrWhiteSpace(connection.NLogTargetName) || name.Equals(connection.NLogTargetName, StringComparison.OrdinalIgnoreCase));
            }) ?? throw new InvalidOperationException("No matching NLog File target was found in the configuration.");

            configuredFile = Attribute(target, "fileName");
            configuredLayout = Attribute(target, "layout");
            configuredEncoding = Attribute(target, "encoding");
            configuredFile = ResolveVariables(configuredFile, variables);
            configuredLayout = ResolveVariables(configuredLayout, variables);
            var nestedLayout = target.Descendants().FirstOrDefault(item => item.Name.LocalName.Equals("layout", StringComparison.OrdinalIgnoreCase));
            if (nestedLayout is not null)
            {
                var nestedType = Attribute(nestedLayout, "type") ?? Attribute(nestedLayout, "xsi:type") ?? string.Empty;
                jsonLayout = nestedType.Contains("JsonLayout", StringComparison.OrdinalIgnoreCase);
            }
        }

        var filePattern = string.IsNullOrWhiteSpace(connection.Endpoint) ? configuredFile : connection.Endpoint;
        if (string.IsNullOrWhiteSpace(filePattern))
        {
            throw new InvalidOperationException("The NLog file target does not define fileName and no file path override was supplied.");
        }

        var layout = string.IsNullOrWhiteSpace(connection.NLogLayout) ? configuredLayout ?? string.Empty : connection.NLogLayout;
        jsonLayout = jsonLayout || layout.Contains("JsonLayout", StringComparison.OrdinalIgnoreCase) ||
                     layout.TrimStart().StartsWith('{');
        return new ResolvedNLogConfiguration(ExpandFilePattern(filePattern, configDirectory), layout, jsonLayout, configDirectory, configuredEncoding);
    }

    public static IReadOnlyList<string> FindFiles(string filePattern, DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null)
    {
        var fullPattern = Path.GetFullPath(filePattern);
        if (Directory.Exists(fullPattern))
        {
            return FilterFiles(Directory.GetFiles(fullPattern, "*.log", SearchOption.TopDirectoryOnly), fromUtc, toUtc)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Take(100)
                .ToArray();
        }

        if (!HasWildcard(fullPattern))
        {
            return File.Exists(fullPattern) && FilterFiles([fullPattern], fromUtc, toUtc).Any() ? [fullPattern] : [];
        }

        var directory = Path.GetDirectoryName(fullPattern);
        var pattern = Path.GetFileName(fullPattern);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return [];
        }

        return FilterFiles(Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly), fromUtc, toUtc)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(100)
            .ToArray();
    }

    private static IEnumerable<string> FilterFiles(IEnumerable<string> files, DateTimeOffset? fromUtc, DateTimeOffset? toUtc)
    {
        if (!fromUtc.HasValue && !toUtc.HasValue) return files;
        var from = fromUtc?.UtcDateTime ?? DateTime.MinValue;
        var to = toUtc?.UtcDateTime ?? DateTime.MaxValue;
        return files.Where(file =>
        {
            var nameMatch = FileDateRegex().Matches(Path.GetFileNameWithoutExtension(file)).Cast<Match>().LastOrDefault();
            if (nameMatch?.Success == true && DateTime.TryParseExact(nameMatch.Value,
                    ["yyyy-MM-dd-HH", "yyyy-MM-dd", "yyyyMMddHH", "yyyyMMdd"],
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeLocal, out var namedAt))
            {
                var fileFrom = namedAt.ToUniversalTime();
                var fileTo = fileFrom.Add(nameMatch.Value.Length is 10 or 8 ? TimeSpan.FromDays(1) : TimeSpan.FromHours(1));
                return fileTo >= from && fileFrom <= to;
            }

            var changedAt = File.GetLastWriteTimeUtc(file);
            return changedAt >= from.AddDays(-1) && changedAt <= to.AddDays(1);
        });
    }

    private static string? ResolveVariables(string? value, IReadOnlyDictionary<string, string> variables)
    {
        if (string.IsNullOrWhiteSpace(value) || variables.Count == 0) return value;
        var resolved = value;
        for (var depth = 0; depth < 10; depth++)
        {
            var changed = false;
            resolved = VariableRegex().Replace(resolved, match =>
            {
                var name = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                if (!variables.TryGetValue(name, out var replacement)) return match.Value;
                changed = true;
                return replacement;
            });
            if (!changed) return resolved;
        }
        throw new InvalidOperationException("NLog variables contain a cycle or exceed the supported nesting depth.");
    }

    private static string ExpandFilePattern(string value, string baseDirectory)
    {
        var expanded = Environment.ExpandEnvironmentVariables(value.Trim());
        expanded = expanded.Replace("${basedir}", baseDirectory, StringComparison.OrdinalIgnoreCase)
            .Replace("${currentdir}", Environment.CurrentDirectory, StringComparison.OrdinalIgnoreCase)
            .Replace("${shortdate}", "*", StringComparison.OrdinalIgnoreCase);
        expanded = NLogTokenRegex().Replace(expanded, "*");
        return Path.IsPathRooted(expanded) ? expanded : Path.Combine(baseDirectory, expanded);
    }

    private static string? Attribute(XElement element, string name) => element.Attributes()
        .FirstOrDefault(item => item.Name.LocalName.Equals(name.Contains(':') ? name[(name.IndexOf(':') + 1)..] : name, StringComparison.OrdinalIgnoreCase))
        ?.Value;

    private static bool HasWildcard(string value) => value.IndexOfAny(['*', '?']) >= 0;

    [GeneratedRegex(@"\$\{[^{}]+\}", RegexOptions.CultureInvariant)]
    private static partial Regex NLogTokenRegex();

    [GeneratedRegex(@"\$\{(?:(?:var|variable):([^}:]+)|([A-Za-z_][A-Za-z0-9_.-]*))\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VariableRegex();

    [GeneratedRegex(@"(?<!\d)(?:\d{4}-\d{2}-\d{2}(?:-\d{2})?|\d{8}(?:\d{2})?)(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex FileDateRegex();
}
