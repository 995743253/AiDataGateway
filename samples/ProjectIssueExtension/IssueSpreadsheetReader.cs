using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ProjectIssueExtension;

public static class IssueSpreadsheetReader
{
    private static readonly XNamespace Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly Regex CellColumn = new("^[A-Z]+", RegexOptions.Compiled);
    private static readonly Dictionary<string, string[]> Headers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TicketNumber"] = ["Q单编号", "Q单号", "问题单号", "单号", "工单编号", "TicketNumber"],
        ["CaseName"] = ["个案书名", "个案名称", "需求名称", "CaseName"],
        ["ProgramName"] = ["程序名称", "功能名称", "模块名称", "ProgramName"],
        ["Description"] = ["问题描述", "问题内容", "问题", "Description"],
        ["TestStatus"] = ["测试状态", "状态", "TestStatus"],
        ["Resolved"] = ["是否解决", "已解决", "Resolved"],
        ["Category"] = ["类别", "类型", "Category"],
        ["Handler"] = ["问题处理人", "处理人", "Handler"],
        ["DevelopmentNote"] = ["开发说明", "开发备注", "DevelopmentNote"],
        ["Owner"] = ["负责顾问", "负责人", "Owner"],
        ["Developer"] = ["开发人员", "开发人", "Developer"],
        ["Remark"] = ["备注", "备注说明", "Remark"],
        ["SolutionNote"] = ["解决描述", "解决说明", "解决方案", "SolutionNote"],
        ["WorkflowStatus"] = ["变动状态", "日报状态", "WorkflowStatus"]
    };

    public static List<(string Sheet, int Row, Dictionary<string, string> Cells)> ReadXlsx(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        if (zip.Entries.Count > 10_000 || zip.Entries.Sum(entry => entry.Length) > 40 * 1024 * 1024)
            throw new ArgumentException("工作簿解压后过大。");
        var workbook = Load(zip, "xl/workbook.xml");
        var rels = Load(zip, "xl/_rels/workbook.xml.rels");
        var targets = rels.Descendants(PackageRelationships + "Relationship")
            .Where(item => item.Attribute("Id") is not null && item.Attribute("Target") is not null)
            .ToDictionary(item => (string)item.Attribute("Id")!, item => (string)item.Attribute("Target")!);
        var shared = zip.GetEntry("xl/sharedStrings.xml") is { } sharedEntry
            ? ReadXml(sharedEntry).Descendants(Spreadsheet + "si").Select(item => string.Concat(item.Descendants(Spreadsheet + "t").Select(text => text.Value))).ToArray()
            : [];
        var result = new List<(string, int, Dictionary<string, string>)>();
        foreach (var sheet in workbook.Descendants(Spreadsheet + "sheet"))
        {
            var name = (string?)sheet.Attribute("name") ?? "Sheet";
            var id = (string?)sheet.Attribute(Relationships + "id");
            if (id is null || !targets.TryGetValue(id, out var target)) continue;
            var path = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
            path = NormalizeZipPath(path);
            var entry = zip.GetEntry(path);
            if (entry is null) continue;
            var document = ReadXml(entry);
            var rawRows = document.Descendants(Spreadsheet + "sheetData").Elements(Spreadsheet + "row")
                .Select(row => (Number: (int?)row.Attribute("r") ?? 0, Cells: row.Elements(Spreadsheet + "c")
                    .ToDictionary(cell => CellColumn.Match(((string?)cell.Attribute("r") ?? "").ToUpperInvariant()).Value,
                        cell => CellValue(cell, shared), StringComparer.OrdinalIgnoreCase)))
                .Where(row => row.Cells.Count > 0).ToArray();
            var header = rawRows.Take(15).Select(row => (Row: row, Score: Score(row.Cells.Values)))
                .OrderByDescending(item => item.Score).FirstOrDefault();
            if (header.Score < 2) continue;
            var columns = header.Row.Cells.Where(cell => !string.IsNullOrWhiteSpace(cell.Value)).ToDictionary(cell => cell.Key, cell => cell.Value);
            foreach (var row in rawRows.Where(row => row.Number > header.Row.Number))
            {
                var cells = columns.ToDictionary(column => column.Value,
                    column => row.Cells.GetValueOrDefault(column.Key) ?? "", StringComparer.OrdinalIgnoreCase);
                if (cells.Values.Any(value => !string.IsNullOrWhiteSpace(value))) result.Add((name, row.Number, cells));
            }
        }
        if (result.Count == 0) throw new ArgumentException("未找到包含问题单字段的工作表，请检查表头。");
        return result;
    }

    public static List<(string Sheet, int Row, Dictionary<string, string> Cells)> ReadText(string text, string sheet)
    {
        text = text.TrimStart('\uFEFF');
        var delimiter = text.Contains('\t') ? '\t' : ',';
        var rawRows = ParseDelimited(text, delimiter).ToArray();
        var headerIndex = rawRows.Take(15).Select((row, index) => (index, score: Score(row))).OrderByDescending(item => item.score).FirstOrDefault();
        if (headerIndex.score < 2) throw new ArgumentException("未找到问题单表头，至少需要两个可识别列（如 Q单编号、个案书名、测试状态）。");
        var headers = rawRows[headerIndex.index];
        var result = new List<(string, int, Dictionary<string, string>)>();
        for (var index = headerIndex.index + 1; index < rawRows.Length; index++)
        {
            var values = rawRows[index];
            var cells = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var column = 0; column < Math.Min(headers.Length, values.Length); column++)
                if (!string.IsNullOrWhiteSpace(headers[column])) cells[headers[column].Trim()] = values[column].Trim();
            if (cells.Values.Any(value => !string.IsNullOrWhiteSpace(value))) result.Add((sheet, index + 1, cells));
        }
        return result;
    }

    public static Issue ToIssue(string projectCode, Dictionary<string, string> cells, string fileName, string sheet, int row, string sourceUrl, string actor, DateTimeOffset now)
    {
        string Pick(string key)
        {
            foreach (var alias in Headers[key])
                if (cells.TryGetValue(alias, out var value) && !string.IsNullOrWhiteSpace(value)) return value.Trim();
            return "";
        }
        var known = Headers.Values.SelectMany(items => items).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new Issue
        {
            Id = Guid.NewGuid().ToString("N"), ProjectCode = projectCode, TicketNumber = Pick("TicketNumber"),
            CaseName = Pick("CaseName"), ProgramName = Pick("ProgramName"), Description = Pick("Description"),
            TestStatus = Pick("TestStatus"), Resolved = Pick("Resolved"), Category = Pick("Category"), Handler = Pick("Handler"),
            DevelopmentNote = Pick("DevelopmentNote"), Owner = Pick("Owner"), Developer = Pick("Developer"), Remark = Pick("Remark"),
            SolutionNote = Pick("SolutionNote"), WorkflowStatus = Pick("WorkflowStatus"),
            SourceUrl = sourceUrl, SourceFileName = fileName, SourceSheet = sheet, SourceRow = row,
            ExtraFields = cells.Where(item => !known.Contains(item.Key) && !string.IsNullOrWhiteSpace(item.Value))
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase),
            CreatedAtUtc = now, UpdatedAtUtc = now, UpdatedBy = actor
        };
    }

    private static int Score(IEnumerable<string> values) => values.Count(value => Headers.Values.Any(aliases => aliases.Any(alias => string.Equals(alias, value.Trim(), StringComparison.OrdinalIgnoreCase))));

    private static string CellValue(XElement cell, string[] shared)
    {
        var type = (string?)cell.Attribute("t");
        if (type == "inlineStr") return string.Concat(cell.Descendants(Spreadsheet + "t").Select(item => item.Value));
        var value = cell.Element(Spreadsheet + "v")?.Value ?? "";
        return type == "s" && int.TryParse(value, out var index) && index >= 0 && index < shared.Length ? shared[index] : value;
    }

    private static XDocument Load(ZipArchive zip, string path) => ReadXml(zip.GetEntry(path) ?? throw new ArgumentException($"Excel 文件缺少 {path}。"));
    private static XDocument ReadXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static string NormalizeZipPath(string path)
    {
        var parts = new Stack<string>();
        foreach (var part in path.Replace('\\', '/').Split('/'))
        {
            if (part == "..") { if (parts.Count > 0) parts.Pop(); }
            else if (part != "." && part.Length > 0) parts.Push(part);
        }
        return string.Join('/', parts.Reverse());
    }

    private static IEnumerable<string[]> ParseDelimited(string text, char delimiter)
    {
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < text.Length; index++)
        {
            var value = text[index];
            if (value == '"')
            {
                if (quoted && index + 1 < text.Length && text[index + 1] == '"') { cell.Append('"'); index++; }
                else quoted = !quoted;
            }
            else if (value == delimiter && !quoted) { row.Add(cell.ToString()); cell.Clear(); }
            else if ((value == '\n' || value == '\r') && !quoted)
            {
                if (value == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
                row.Add(cell.ToString()); cell.Clear();
                if (row.Any(item => item.Length > 0)) yield return row.ToArray();
                row.Clear();
            }
            else cell.Append(value);
        }
        row.Add(cell.ToString());
        if (row.Any(item => item.Length > 0)) yield return row.ToArray();
    }
}
