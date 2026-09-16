using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Xml.Linq;
using AiDataGateway.LocalLogViewer.Models;

namespace AiDataGateway.LocalLogViewer.Services
{
    public sealed class LocalLogReader
    {
        private const int MaximumResultCount = 10000;
        private static readonly Regex LayoutTokenRegex = new Regex(@"\$\{([^{}]+)\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex DateInPathRegex = new Regex(@"(?<!\d)(?:\d{4}-\d{2}-\d{2}(?:[-_ ]\d{2})?|\d{8}(?:\d{2})?)(?!\d)", RegexOptions.Compiled);
        private static readonly Regex TimestampStartRegex = new Regex(@"^\s*\[?\d{4}[-/]\d{2}[-/]\d{2}[ T]", RegexOptions.Compiled);
        private static readonly Regex CommonEnvelopeRegex = new Regex(@"\A(?<timestamp>\d{4}[-/]\d{2}[-/]\d{2}[ T]\d{2}:\d{2}:\d{2}(?:[.,]\d+)?)\s*\|\s*(?<level>Trace|Debug|Info|Information|Warn|Warning|Error|Fatal)(?:\[(?<threadid>[^\]\r\n]*)\])?(?<body>[\s\S]*)\z", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex BracketPrefixRegex = new Regex(@"^\s*(?:\[(?<value>[^\]\r\n]{1,300})\]\s*)+", RegexOptions.Compiled);
        private static readonly Regex StackFunctionRegex = new Regex(@"(?:\bat\s+|在\s*)(?<value>[A-Za-z_][A-Za-z0-9_`.+<>]*(?:::[A-Za-z_][A-Za-z0-9_`.+<>]*)?)\s*\(", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly string[] KnownLevels = { "Trace", "Debug", "Info", "Information", "Warn", "Warning", "Error", "Fatal" };
        private readonly StructuredValueService _structuredValues = new StructuredValueService();
        private readonly LogSqlService _sqlService = new LogSqlService();
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024, RecursionLimit = 64 };

        public Task<LogReadResult> ReadAsync(LogProfile profile, DateTime? from, DateTime? to, string level, string search, CancellationToken cancellationToken)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            return Task.Run(() => Read(profile, from, to, level, search, cancellationToken, true), cancellationToken);
        }

        public Task<LogReadResult> ReadRecentAsync(LogProfile profile, TimeSpan duration, string level, string search, CancellationToken cancellationToken)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
            return Task.Run(() =>
            {
                var latest = FindLatestTimestamp(profile, cancellationToken);
                if (!latest.HasValue) return new LogReadResult { Warning = "未能从日志内容中识别有效时间" };
                var from = latest.Value.Subtract(duration);
                var result = Read(profile, from, latest.Value, level, search, cancellationToken, false);
                result.LatestTimestamp = latest;
                result.RangeStart = from;
                result.RangeEnd = latest;
                return result;
            }, cancellationToken);
        }

        private LogReadResult Read(LogProfile profile, DateTime? from, DateTime? to, string level, string search, CancellationToken cancellationToken, bool endAtEndOfDay)
        {
            var resolved = Resolve(profile);
            var files = FindFiles(resolved.FilePattern, profile.FilePattern, from, to).Take(500).ToArray();
            var result = new LogReadResult { FilesScanned = files.Length };
            var skipped = new List<string>();

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var info = new FileInfo(file);
                var maximumBytes = Math.Max(1, Math.Min(10, profile.MaximumReadMegabytesPerFile)) * 1024L * 1024L;
                if (info.Length > maximumBytes)
                {
                    skipped.Add(info.Name);
                    continue;
                }

                var text = ReadShared(file, DetectEncoding(file, string.IsNullOrWhiteSpace(profile.EncodingName) ? resolved.EncodingName : profile.EncodingName));
                var entries = resolved.JsonLayout || LooksLikeJson(text)
                    ? ParseJsonDocuments(text, file)
                    : ParseTextRecords(text, resolved.Layout, file);

                foreach (var entry in entries)
                {
                    _sqlService.Populate(entry);
                    if (!Matches(entry, from, to, level, search, endAtEndOfDay)) continue;
                    result.Items.Add(entry);
                    if (result.Items.Count >= MaximumResultCount)
                    {
                        result.Truncated = true;
                        break;
                    }
                }
                if (result.Truncated) break;
            }

            result.Items = result.Items.OrderByDescending(item => item.Timestamp ?? DateTime.MinValue).ToList();
            result.LatestTimestamp = result.Items.Where(item => item.Timestamp.HasValue).Select(item => item.Timestamp).FirstOrDefault();
            var warnings = new List<string>();
            if (skipped.Count > 0) warnings.Add(skipped.Count + " 个文件超过单文件读取上限，已跳过");
            if (result.Truncated) warnings.Add("结果超过 10000 条，仅显示最新部分");
            result.Warning = string.Join("；", warnings);
            return result;
        }

        private DateTime? FindLatestTimestamp(LogProfile profile, CancellationToken cancellationToken)
        {
            var resolved = Resolve(profile);
            DateTime? latest = null;
            foreach (var file in FindFiles(resolved.FilePattern, profile.FilePattern, null, null).Take(16))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var info = new FileInfo(file);
                var maximumBytes = Math.Max(1, Math.Min(10, profile.MaximumReadMegabytesPerFile)) * 1024L * 1024L;
                if (info.Length > maximumBytes) continue;
                var text = ReadShared(file, DetectEncoding(file, string.IsNullOrWhiteSpace(profile.EncodingName) ? resolved.EncodingName : profile.EncodingName));
                var entries = resolved.JsonLayout || LooksLikeJson(text) ? ParseJsonDocuments(text, file) : ParseTextRecords(text, resolved.Layout, file);
                var fileLatest = entries.Where(item => item.Timestamp.HasValue).Select(item => item.Timestamp.Value).OrderByDescending(item => item).FirstOrDefault();
                if (fileLatest != default(DateTime) && (!latest.HasValue || fileLatest > latest.Value)) latest = fileLatest;
            }
            return latest;
        }

        private ResolvedLogProfile Resolve(LogProfile profile)
        {
            var root = Environment.ExpandEnvironmentVariables((profile.RootDirectory ?? string.Empty).Trim());
            if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("请先设置日志目录。");
            root = Path.GetFullPath(root);
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException("日志目录不存在：" + root);

            var configPath = Environment.ExpandEnvironmentVariables((profile.NLogConfigurationPath ?? string.Empty).Trim());
            if (string.IsNullOrWhiteSpace(configPath))
            {
                configPath = Directory.EnumerateFiles(root, "*.config", SearchOption.AllDirectories)
                    .FirstOrDefault(path => Path.GetFileName(path).IndexOf("nlog", StringComparison.OrdinalIgnoreCase) >= 0);
            }

            var filePattern = Path.Combine(root, string.IsNullOrWhiteSpace(profile.FilePattern) ? "*.log" : profile.FilePattern);
            var layout = profile.LayoutOverride ?? string.Empty;
            var encoding = profile.EncodingName;
            var json = false;
            if (!string.IsNullOrWhiteSpace(configPath))
            {
                configPath = Path.GetFullPath(configPath);
                if (!File.Exists(configPath)) throw new FileNotFoundException("找不到 NLog 配置文件。", configPath);
                var document = XDocument.Load(configPath, LoadOptions.PreserveWhitespace);
                var variables = document.Descendants().Where(element => EqualName(element, "variable"))
                    .Where(element => !string.IsNullOrWhiteSpace(Attribute(element, "name")))
                    .ToDictionary(element => Attribute(element, "name"), element => Attribute(element, "value") ?? string.Empty, StringComparer.OrdinalIgnoreCase);
                var target = document.Descendants().Where(element => EqualName(element, "target")).FirstOrDefault(element =>
                {
                    var type = Attribute(element, "type") ?? string.Empty;
                    var name = Attribute(element, "name") ?? string.Empty;
                    return type.IndexOf("File", StringComparison.OrdinalIgnoreCase) >= 0 &&
                           (string.IsNullOrWhiteSpace(profile.NLogTargetName) || string.Equals(name, profile.NLogTargetName, StringComparison.OrdinalIgnoreCase));
                });
                if (target != null)
                {
                    var configuredFile = ResolveVariables(Attribute(target, "fileName"), variables);
                    var configDirectory = Path.GetDirectoryName(configPath);
                    if (!string.IsNullOrWhiteSpace(configuredFile) && string.IsNullOrWhiteSpace(profile.RootDirectory))
                        filePattern = ExpandNLogPath(configuredFile, configDirectory);
                    if (string.IsNullOrWhiteSpace(profile.LayoutOverride)) layout = ResolveVariables(Attribute(target, "layout"), variables) ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(profile.EncodingName)) encoding = Attribute(target, "encoding");
                    json = target.Descendants().Any(element => EqualName(element, "layout") && ((Attribute(element, "type") ?? string.Empty).IndexOf("JsonLayout", StringComparison.OrdinalIgnoreCase) >= 0));
                }
            }
            return new ResolvedLogProfile(filePattern, layout, json || layout.TrimStart().StartsWith("{", StringComparison.Ordinal), encoding);
        }

        private static IEnumerable<string> FindFiles(string basePattern, string requestedPattern, DateTime? from, DateTime? to)
        {
            var root = GetSearchRoot(basePattern);
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return Enumerable.Empty<string>();
            var pattern = Path.GetFileName(basePattern);
            if (string.IsNullOrWhiteSpace(pattern) || pattern.IndexOf("${", StringComparison.Ordinal) >= 0) pattern = string.IsNullOrWhiteSpace(requestedPattern) ? "*.log" : requestedPattern;
            if (pattern.IndexOfAny(new[] { '\\', '/' }) >= 0) pattern = "*.log";
            try
            {
                return Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
                    .Where(path => FileInRange(path, from, to))
                    .OrderByDescending(FileDateKey)
                    .ToArray();
            }
            catch (UnauthorizedAccessException) { return Enumerable.Empty<string>(); }
            catch (IOException) { return Enumerable.Empty<string>(); }
        }

        private static DateTime FileDateKey(string path)
        {
            var match = DateInPathRegex.Matches(path).Cast<Match>().LastOrDefault();
            DateTime date;
            if (match != null && DateTime.TryParseExact(match.Value.Replace('_', '-').Replace(' ', '-'),
                    new[] { "yyyy-MM-dd-HH", "yyyy-MM-dd", "yyyyMMddHH", "yyyyMMdd" }, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal, out date)) return date;
            return File.GetLastWriteTime(path);
        }

        private static bool FileInRange(string path, DateTime? from, DateTime? to)
        {
            if (!from.HasValue && !to.HasValue) return true;
            var match = DateInPathRegex.Matches(path).Cast<Match>().LastOrDefault();
            DateTime date;
            if (match != null && DateTime.TryParseExact(match.Value.Replace('_', '-').Replace(' ', '-'),
                    new[] { "yyyy-MM-dd-HH", "yyyy-MM-dd", "yyyyMMddHH", "yyyyMMdd" }, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal, out date))
            {
                var end = date.Add(match.Value.Length <= 10 ? TimeSpan.FromDays(1) : TimeSpan.FromHours(1));
                return (!from.HasValue || end >= from.Value.Date) && (!to.HasValue || date < to.Value.Date.AddDays(1));
            }
            var changed = File.GetLastWriteTime(path);
            return (!from.HasValue || changed >= from.Value.Date.AddDays(-1)) && (!to.HasValue || changed < to.Value.Date.AddDays(2));
        }

        private IList<LogEntry> ParseJsonDocuments(string text, string file)
        {
            var results = new List<LogEntry>();
            foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var normalized = _structuredValues.Normalize(_serializer.DeserializeObject(line.Trim()));
                    var dictionary = normalized as IDictionary<string, object> ?? new Dictionary<string, object> { { "value", normalized } };
                    results.Add(FromFields(dictionary, line, file));
                }
                catch
                {
                    results.Add(Unparsed(line, file, "JSON 记录无法解析"));
                }
            }
            return results;
        }

        private IList<LogEntry> ParseTextRecords(string text, string layout, string file)
        {
            var parser = new LayoutParser(layout);
            var records = SplitRecords(text, layout);
            var results = new List<LogEntry>();
            foreach (var record in records)
            {
                IDictionary<string, object> fields = parser.Parse(record);
                if (fields.Count == 0 || PollutedLevel(fields)) fields = ParseCommonEnvelope(record);
                if (fields.Count == 0) fields["message"] = record;
                foreach (var key in fields.Keys.ToArray())
                {
                    var original = fields[key];
                    var normalized = _structuredValues.Normalize(original);
                    if (string.Equals(key, "message", StringComparison.OrdinalIgnoreCase) && !(normalized is string))
                    {
                        fields["_messageJson"] = normalized;
                        fields[key] = original;
                    }
                    else fields[key] = normalized;
                }
                if (!fields.ContainsKey("level")) fields["level"] = InferLevel(file);
                results.Add(FromFields(fields, record, file));
            }
            return results;
        }

        private LogEntry FromFields(IDictionary<string, object> fields, string raw, string file)
        {
            var message = StringValue(Get(fields, "message", "Message", "@m", "RenderedMessage", "@mt"));
            if (string.IsNullOrEmpty(message)) message = raw;
            var properties = new Dictionary<string, object>(fields, StringComparer.OrdinalIgnoreCase);
            properties["_file"] = file;
            var normalizedMessage = _structuredValues.Normalize(message);
            if (!(normalizedMessage is string)) properties["_messageJson"] = normalizedMessage;
            var functionName = FindFunctionName(properties);
            if (!string.IsNullOrWhiteSpace(functionName)) properties["_function"] = functionName;
            return new LogEntry
            {
                Id = EventId(file + "\n" + raw),
                Timestamp = ParseTimestamp(StringValue(Get(fields, "timestamp", "longdate", "date", "time", "@t", "Timestamp"))),
                Level = StringValue(Get(fields, "level", "Level", "@l")) ?? InferLevel(file) ?? "Info",
                Message = message,
                Exception = StringValue(Get(fields, "exception", "Exception", "@x")),
                FunctionName = functionName,
                FilePath = file,
                RawText = raw,
                Properties = properties
            };
        }

        private static string FindFunctionName(IDictionary<string, object> properties)
        {
            if (properties == null) return null;
            var preferredNames = new[] { "callsite", "caller", "membername", "method", "function", "actionname", "sourcecontext", "logger", "loggername", "categoryname" };
            foreach (var preferredName in preferredNames)
            {
                var value = FindNamedValue(properties, preferredName, 0);
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }
            var fromMessage = FindFunctionInText(StringValue(Get(properties, "message", "Message", "@m", "RenderedMessage", "@mt")));
            if (!string.IsNullOrWhiteSpace(fromMessage)) return fromMessage;
            return FindFunctionInStack(StringValue(Get(properties, "exception", "Exception", "@x")));
        }

        private static string FindFunctionInText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var match = BracketPrefixRegex.Match(text);
            if (!match.Success) return null;
            var candidates = match.Groups["value"].Captures.Cast<Capture>()
                .Select(item => item.Value.Trim())
                .Where(item => item.Length > 0 && item.Any(char.IsLetter) && (item.Contains(".") || item.Contains("::")))
                .Where(item => !Regex.IsMatch(item, @"^\d+(?:\.\d+)+$"))
                .ToList();
            return candidates.LastOrDefault();
        }

        private static string FindFunctionInStack(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var match = StackFunctionRegex.Match(text);
            return match.Success ? match.Groups["value"].Value.Trim() : null;
        }

        private static string FindNamedValue(object value, string name, int depth)
        {
            if (value == null || depth > 8) return null;
            var dictionary = value as IDictionary<string, object>;
            if (dictionary != null)
            {
                foreach (var pair in dictionary)
                    if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                    {
                        var text = StringValue(pair.Value);
                        if (!string.IsNullOrWhiteSpace(text)) return text;
                    }
                foreach (var pair in dictionary)
                {
                    var nested = FindNamedValue(pair.Value, name, depth + 1);
                    if (!string.IsNullOrWhiteSpace(nested)) return nested;
                }
            }
            var list = value as System.Collections.IEnumerable;
            if (list != null && !(value is string))
                foreach (var item in list)
                {
                    var nested = FindNamedValue(item, name, depth + 1);
                    if (!string.IsNullOrWhiteSpace(nested)) return nested;
                }
            return null;
        }

        private static IDictionary<string, object> ParseCommonEnvelope(string raw)
        {
            var fields = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var match = CommonEnvelopeRegex.Match(raw);
            if (!match.Success) return fields;
            fields["timestamp"] = match.Groups["timestamp"].Value;
            fields["level"] = match.Groups["level"].Value;
            if (match.Groups["threadid"].Success) fields["threadid"] = match.Groups["threadid"].Value;
            var body = match.Groups["body"].Value;
            var split = body.LastIndexOf('|');
            fields["message"] = split < 0 ? body : body.Substring(0, split);
            if (split >= 0 && split + 1 < body.Length) fields["exception"] = body.Substring(split + 1);
            return fields;
        }

        private static IList<string> SplitRecords(string text, string layout)
        {
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            Regex start;
            var prefix = (layout ?? string.Empty).TrimStart();
            if (prefix.StartsWith("${level", StringComparison.OrdinalIgnoreCase)) start = new Regex(@"^(Trace|Debug|Info|Information|Warn|Warning|Error|Fatal)\b", RegexOptions.IgnoreCase);
            else start = TimestampStartRegex;
            var results = new List<string>();
            var current = new StringBuilder();
            foreach (var line in lines)
            {
                var begins = start.IsMatch(line);
                if (begins && current.Length > 0) { results.Add(current.ToString().TrimEnd('\r', '\n')); current.Clear(); }
                if (current.Length > 0 || begins || !string.IsNullOrWhiteSpace(line)) current.AppendLine(line);
            }
            if (current.Length > 0) results.Add(current.ToString().TrimEnd('\r', '\n'));
            return results;
        }

        private static bool Matches(LogEntry entry, DateTime? from, DateTime? to, string level, string search, bool endAtEndOfDay)
        {
            if (!string.IsNullOrWhiteSpace(level) && !string.Equals(level, "全部", StringComparison.OrdinalIgnoreCase) && !string.Equals(entry.Level, level, StringComparison.OrdinalIgnoreCase)) return false;
            if (!endAtEndOfDay && !entry.Timestamp.HasValue) return false;
            if (from.HasValue && entry.Timestamp.HasValue && entry.Timestamp.Value < from.Value) return false;
            if (to.HasValue && entry.Timestamp.HasValue && entry.Timestamp.Value > (endAtEndOfDay ? to.Value.Date.AddDays(1).AddTicks(-1) : to.Value)) return false;
            if (!string.IsNullOrWhiteSpace(search) && (entry.RawText ?? string.Empty).IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) return false;
            return true;
        }

        private static string ReadShared(string path, Encoding encoding)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, encoding, true)) return reader.ReadToEnd();
        }

        private static Encoding DetectEncoding(string path, string configured)
        {
            if (!string.IsNullOrWhiteSpace(configured)) return Encoding.GetEncoding(configured.Trim());
            var bytes = new byte[Math.Min(65536, (int)Math.Min(new FileInfo(path).Length, int.MaxValue))];
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) stream.Read(bytes, 0, bytes.Length);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8;
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode;
            try { new UTF8Encoding(false, true).GetString(bytes); return new UTF8Encoding(false); }
            catch (DecoderFallbackException) { return CultureInfo.CurrentCulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? Encoding.GetEncoding("GB18030") : Encoding.Default; }
        }

        private static string GetSearchRoot(string pattern)
        {
            var wildcard = pattern.IndexOfAny(new[] { '*', '?' });
            if (wildcard < 0)
            {
                var full = Path.GetFullPath(pattern);
                return Directory.Exists(full) ? full : Path.GetDirectoryName(full);
            }
            var separator = pattern.LastIndexOfAny(new[] { '\\', '/' }, wildcard);
            var root = separator < 0 ? "." : pattern.Substring(0, separator);
            return Path.GetFullPath(root);
        }

        private static string ExpandNLogPath(string value, string baseDirectory)
        {
            var expanded = Environment.ExpandEnvironmentVariables(value).Replace("${basedir}", baseDirectory).Replace("${shortdate}", "*");
            expanded = LayoutTokenRegex.Replace(expanded, "*");
            return Path.IsPathRooted(expanded) ? expanded : Path.Combine(baseDirectory, expanded);
        }

        private static string ResolveVariables(string value, IDictionary<string, string> variables)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            return Regex.Replace(value, @"\$\{(?:var|variable):([^}:]+)\}", match => variables.ContainsKey(match.Groups[1].Value) ? variables[match.Groups[1].Value] : match.Value, RegexOptions.IgnoreCase);
        }

        private static object Get(IDictionary<string, object> fields, params string[] names)
        {
            foreach (var name in names) { object value; if (fields.TryGetValue(name, out value)) return value; }
            return null;
        }

        private static string StringValue(object value) { return value == null ? null : value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture); }
        private static DateTime? ParseTimestamp(string value) { DateTime result; return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out result) ? result : (DateTime?)null; }
        private static bool LooksLikeJson(string value) { var text = (value ?? string.Empty).TrimStart(); return text.StartsWith("{") || text.StartsWith("["); }
        private static string InferLevel(string file) { var name = Path.GetFileName(file); return KnownLevels.FirstOrDefault(level => name.StartsWith(level, StringComparison.OrdinalIgnoreCase)); }
        private static bool PollutedLevel(IDictionary<string, object> fields) { var level = StringValue(Get(fields, "level")); return level != null && !KnownLevels.Any(item => string.Equals(item, level.Trim(), StringComparison.OrdinalIgnoreCase)); }
        private static bool EqualName(XElement element, string name) { return string.Equals(element.Name.LocalName, name, StringComparison.OrdinalIgnoreCase); }
        private static string Attribute(XElement element, string name) { var item = element.Attributes().FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, name.Contains(":") ? name.Substring(name.IndexOf(':') + 1) : name, StringComparison.OrdinalIgnoreCase)); return item == null ? null : item.Value; }
        private static string EventId(string text) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", string.Empty).Substring(0, 24).ToLowerInvariant(); }
        private static LogEntry Unparsed(string raw, string file, string warning) { return new LogEntry { Id = EventId(file + raw), Message = raw.Trim(), FilePath = file, RawText = raw, Incomplete = true, Properties = new Dictionary<string, object> { { "_file", file }, { "_warning", warning } } }; }

        private sealed class ResolvedLogProfile
        {
            public ResolvedLogProfile(string filePattern, string layout, bool jsonLayout, string encodingName) { FilePattern = filePattern; Layout = layout; JsonLayout = jsonLayout; EncodingName = encodingName; }
            public string FilePattern { get; private set; }
            public string Layout { get; private set; }
            public bool JsonLayout { get; private set; }
            public string EncodingName { get; private set; }
        }

        private sealed class LayoutParser
        {
            private readonly Regex _regex;
            private readonly IList<KeyValuePair<string, string>> _fields = new List<KeyValuePair<string, string>>();
            public LayoutParser(string layout)
            {
                if (string.IsNullOrWhiteSpace(layout)) return;
                var expression = new StringBuilder("\\A");
                var position = 0;
                var index = 0;
                foreach (Match match in LayoutTokenRegex.Matches(layout))
                {
                    expression.Append(Regex.Escape(layout.Substring(position, match.Index - position)));
                    var group = "f" + index++;
                    var token = match.Groups[1].Value;
                    expression.Append("(?<" + group + ">.*?)");
                    _fields.Add(new KeyValuePair<string, string>(group, NormalizeName(token, index)));
                    position = match.Index + match.Length;
                }
                expression.Append(Regex.Escape(layout.Substring(position))).Append("\\z");
                _regex = new Regex(expression.ToString(), RegexOptions.Singleline | RegexOptions.CultureInvariant);
            }
            public IDictionary<string, object> Parse(string raw)
            {
                var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                if (_regex == null) return result;
                var match = _regex.Match(raw);
                if (!match.Success) return result;
                foreach (var field in _fields) result[field.Value] = match.Groups[field.Key].Value;
                return result;
            }
            private static string NormalizeName(string token, int index)
            {
                var main = token.Split(':')[0].Trim().ToLowerInvariant();
                if (main == "longdate") return "timestamp";
                if (main == "event-properties")
                {
                    var match = Regex.Match(token, @"item=([^}:]+)", RegexOptions.IgnoreCase);
                    if (match.Success) return match.Groups[1].Value;
                }
                return string.IsNullOrWhiteSpace(main) ? "field" + index : main;
            }
        }
    }
}
