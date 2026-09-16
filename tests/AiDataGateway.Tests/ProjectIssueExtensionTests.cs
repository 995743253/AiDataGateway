using System.IO.Compression;
using System.Text;
using System.Text.Json;
using AiDataGateway.Extensions;
using ProjectIssueExtension;

namespace AiDataGateway.Tests;

public sealed class ProjectIssueExtensionTests
{
    [Fact]
    public void ParsesOnlineTablePasteWithQuotedMultilineCell()
    {
        const string text = "Q单编号\t测试状态\t是否解决\t个案书名\t程序名称\t自定义字段\n" +
                            "QTB20260522040\t已完成\t否\t质量异常单\tMES-工单新增\t重点\n" +
                            "QTB20260522041\t待复测\t是\t备注传递\t备注传递\t\"第一行\n第二行\"";
        var rows = IssueSpreadsheetReader.ReadText(text, "在线表格.tsv");
        Assert.Equal(2, rows.Count);
        var first = IssueSpreadsheetReader.ToIssue("mes", rows[0].Cells, "在线表格.tsv", rows[0].Sheet, rows[0].Row,
            "https://www.kdocs.cn/l/example", "tester", DateTimeOffset.UtcNow);
        Assert.Equal("QTB20260522040", first.TicketNumber);
        Assert.Equal("质量异常单", first.CaseName);
        Assert.Equal("重点", first.ExtraFields["自定义字段"]);
        Assert.Equal("第一行\n第二行", rows[1].Cells["自定义字段"]);
        // 导入列未提供变动状态时保持为空，不带变动时间。
        Assert.Equal("", first.WorkflowStatus);
        Assert.Null(first.WorkflowStatusChangedAtUtc);
    }

    [Fact]
    public void ParsesWorkflowAndSolutionColumnsWhenPresent()
    {
        const string text = "Q单编号\t测试状态\t变动状态\t解决描述\t提单日期\t完成日期\t个案书名\nQTB100\t已修复\t处理完成\t已修复并回归通过\t2026/9/1\t2026-09-12\t工单新增";
        var rows = IssueSpreadsheetReader.ReadText(text, "导入.tsv");
        var issue = IssueSpreadsheetReader.ToIssue("mes", rows[0].Cells, "导入.tsv", rows[0].Sheet, rows[0].Row, "", "tester", DateTimeOffset.UtcNow);
        Assert.Equal("处理完成", issue.WorkflowStatus);
        Assert.Equal("已修复并回归通过", issue.SolutionNote);
        Assert.Equal("2026/9/1", issue.RaisedDate);
        Assert.Equal("2026-09-12", issue.CompletedDate);
    }

    [Fact]
    public void FindsHeaderOnSecondRowInSharedStringXlsx()
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            Add(zip, "xl/workbook.xml", "<workbook xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'><sheets><sheet name='问题清单' sheetId='1' r:id='rId1'/></sheets></workbook>");
            Add(zip, "xl/_rels/workbook.xml.rels", "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='rId1' Target='worksheets/sheet1.xml' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet'/></Relationships>");
            Add(zip, "xl/sharedStrings.xml", "<sst xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><si><t>MES</t></si><si><t>测试状态</t></si><si><t>Q单编号</t></si><si><t>个案书名</t></si><si><t>已完成</t></si><si><t>QTB20260522040</t></si><si><t>质量异常单</t></si></sst>");
            Add(zip, "xl/worksheets/sheet1.xml", "<worksheet xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><sheetData><row r='1'><c r='A1' t='s'><v>0</v></c></row><row r='2'><c r='A2' t='s'><v>1</v></c><c r='B2' t='s'><v>2</v></c><c r='C2' t='s'><v>3</v></c></row><row r='3'><c r='A3' t='s'><v>4</v></c><c r='B3' t='s'><v>5</v></c><c r='C3' t='s'><v>6</v></c></row></sheetData></worksheet>");
        }
        var rows = IssueSpreadsheetReader.ReadXlsx(memory.ToArray());
        Assert.Single(rows);
        Assert.Equal(3, rows[0].Row);
        Assert.Equal("QTB20260522040", rows[0].Cells["Q单编号"]);
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        using var stream = zip.CreateEntry(name).Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes);
    }

    [Fact]
    public async Task ImportDeduplicatesByProjectAndTicketAndCanBeQueried()
    {
        var module = new ProjectIssueModule();
        var context = new TestContext();
        var first = JsonSerializer.SerializeToElement(new
        {
            projectCode = "mes", fileName = "问题清单.tsv",
            text = "Q单编号\t测试状态\t个案书名\nQTB001\t待开发处理\t工单新增",
            sourceUrl = "https://www.kdocs.cn/l/example"
        });
        var result = await module.InvokeAsync("import_issues", first, context, CancellationToken.None);
        Assert.Equal(1, result.GetProperty("created").GetInt32());
        var second = JsonSerializer.SerializeToElement(new
        {
            projectCode = "mes", fileName = "问题清单.tsv",
            text = "Q单编号\t测试状态\t个案书名\nQTB001\t已完成\t工单新增",
            sourceUrl = "https://www.kdocs.cn/l/example"
        });
        result = await module.InvokeAsync("import_issues", second, context, CancellationToken.None);
        Assert.Equal(1, result.GetProperty("updated").GetInt32());
        var list = await module.InvokeAsync("list_issues", JsonSerializer.SerializeToElement(new { projectCode = "mes", status = "已完成" }), context, CancellationToken.None);
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        Assert.Equal("QTB001", list.GetProperty("items")[0].GetProperty("ticketNumber").GetString());
        var summary = await module.InvokeAsync("summarize_issues", JsonSerializer.SerializeToElement(new { projectCode = "mes" }), context, CancellationToken.None);
        Assert.Equal(1, summary.GetProperty("total").GetInt32());
        Assert.Equal("已完成", summary.GetProperty("statuses")[0].GetProperty("name").GetString());
        // 人工表单写入与导入仅限界面；AI 通过 update_issue / set_workflow_status 维护单据。
        var uiWriteTools = module.Definition.Tools.Where(tool => !tool.ReadOnly).ToArray();
        Assert.All(uiWriteTools.Where(tool => tool.Name is "save_issue" or "delete_issue" or "import_issues"), tool => Assert.False(tool.VisibleInMcp));
        Assert.All(uiWriteTools.Where(tool => tool.Name is "update_issue" or "set_workflow_status"), tool => Assert.True(tool.VisibleInMcp));
    }

    [Fact]
    public async Task ImportWithoutWorkflowColumnKeepsExistingWorkflowState()
    {
        var module = new ProjectIssueModule();
        var context = new TestContext();
        var create = JsonSerializer.SerializeToElement(new { projectCode = "mes", ticketNumber = "QTB200", workflowStatus = "处理完成", testStatus = "已修复" });
        await module.InvokeAsync("update_issue", create, context, CancellationToken.None);
        var created = await module.InvokeAsync("list_issues", JsonSerializer.SerializeToElement(new { projectCode = "mes" }), context, CancellationToken.None);
        var changedAt = created.GetProperty("items")[0].GetProperty("workflowStatusChangedAtUtc").GetDateTimeOffset();

        var import = JsonSerializer.SerializeToElement(new
        {
            projectCode = "mes", fileName = "回导.tsv",
            text = "Q单编号\t测试状态\t个案书名\nQTB200\t已关闭\t工单新增"
        });
        var result = await module.InvokeAsync("import_issues", import, context, CancellationToken.None);
        Assert.Equal(1, result.GetProperty("updated").GetInt32());
        var list = await module.InvokeAsync("list_issues", JsonSerializer.SerializeToElement(new { projectCode = "mes" }), context, CancellationToken.None);
        var item = list.GetProperty("items")[0];
        Assert.Equal("已关闭", item.GetProperty("testStatus").GetString());
        Assert.Equal("处理完成", item.GetProperty("workflowStatus").GetString());
        Assert.Equal(changedAt, item.GetProperty("workflowStatusChangedAtUtc").GetDateTimeOffset());
    }

    [Fact]
    public async Task WorkflowStatusChangeTimeOnlyMovesWhenValueChanges()
    {
        var module = new ProjectIssueModule();
        var context = new TestContext();
        var create = JsonSerializer.SerializeToElement(new { projectCode = "mes", ticketNumber = "QTB300", caseName = "工单新增", workflowStatus = "未处理" });
        await module.InvokeAsync("update_issue", create, context, CancellationToken.None);
        var first = await ReadSingleAsync(module, context);
        var firstChangedAt = first.GetProperty("workflowStatusChangedAtUtc").GetDateTimeOffset();
        Assert.True(firstChangedAt > DateTimeOffset.UtcNow.AddSeconds(-5));

        // 同值重复维护：变动时间保持不变。
        await module.InvokeAsync("set_workflow_status", JsonSerializer.SerializeToElement(new { projectCode = "mes", ticketNumber = "QTB300", workflowStatus = "未处理" }), context, CancellationToken.None);
        var unchanged = await ReadSingleAsync(module, context);
        Assert.Equal(firstChangedAt, unchanged.GetProperty("workflowStatusChangedAtUtc").GetDateTimeOffset());

        // 其他字段修改：变动时间保持不变。
        await module.InvokeAsync("update_issue", JsonSerializer.SerializeToElement(new { projectCode = "mes", ticketNumber = "QTB300", solutionNote = "已定位，等待发版", testStatus = "已修复" }), context, CancellationToken.None);
        var edited = await ReadSingleAsync(module, context);
        Assert.Equal(firstChangedAt, edited.GetProperty("workflowStatusChangedAtUtc").GetDateTimeOffset());
        Assert.Equal("已定位，等待发版", edited.GetProperty("solutionNote").GetString());

        // 状态实际变化：记录新的变动时间并追加历史。
        await Task.Delay(20);
        var response = await module.InvokeAsync("set_workflow_status", JsonSerializer.SerializeToElement(new { projectCode = "mes", ticketNumber = "QTB300", workflowStatus = "处理完成" }), context, CancellationToken.None);
        Assert.True(response.GetProperty("changed").GetBoolean());
        var moved = await ReadSingleAsync(module, context);
        Assert.True(moved.GetProperty("workflowStatusChangedAtUtc").GetDateTimeOffset() > firstChangedAt);
        Assert.Equal(2, moved.GetProperty("workflowHistory").GetArrayLength());
        Assert.Equal("", moved.GetProperty("workflowHistory")[0].GetProperty("from").GetString());
        Assert.Equal("未处理", moved.GetProperty("workflowHistory")[1].GetProperty("from").GetString());
        Assert.Equal("处理完成", moved.GetProperty("workflowHistory")[1].GetProperty("to").GetString());
    }

    [Fact]
    public async Task DailyReportIncludesOnlyIssuesWhoseWorkflowChangedThatDay()
    {
        var module = new ProjectIssueModule();
        var context = new TestContext();
        var offset = TimeZoneInfo.Local.GetUtcOffset(DateTimeOffset.Now);
        var today = DateTimeOffset.Now;
        var yesterday = today.AddDays(-1);
        await context.Storage.WriteAsync("issues", JsonSerializer.SerializeToElement(new List<Issue>
        {
            new()
            {
                Id = "yesterday", ProjectCode = "mes", TicketNumber = "QTB000", CaseName = "昨日完成",
                WorkflowStatus = "处理完成", WorkflowStatusChangedAtUtc = new DateTimeOffset(yesterday.Date.AddHours(10), offset)
            },
            new()
            {
                Id = "today", ProjectCode = "mes", TicketNumber = "QTB111", CaseName = "今日完成",
                WorkflowStatus = "处理完成", WorkflowStatusChangedAtUtc = new DateTimeOffset(today.Date.AddHours(9), offset),
                SolutionNote = "已修复并回归"
            },
            new() { Id = "never", ProjectCode = "mes", TicketNumber = "QTB222", CaseName = "未变动", WorkflowStatus = "未处理" }
        }));
        var report = await module.InvokeAsync("daily_report", JsonSerializer.SerializeToElement(new { projectCode = "mes" }), context, CancellationToken.None);
        Assert.Equal(today.Date.ToString("yyyy-MM-dd"), report.GetProperty("date").GetString());
        Assert.Equal(1, report.GetProperty("total").GetInt32());
        var item = report.GetProperty("items")[0];
        Assert.Equal("QTB111", item.GetProperty("ticketNumber").GetString());
        Assert.Equal("已修复并回归", item.GetProperty("solutionNote").GetString());
        Assert.Equal(1, report.GetProperty("byStatus")[0].GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task DailyReportWithoutProjectAggregatesAllProjects()
    {
        var module = new ProjectIssueModule();
        var context = new TestContext();
        var offset = TimeZoneInfo.Local.GetUtcOffset(DateTimeOffset.Now);
        var today = DateTimeOffset.Now;
        await context.Storage.WriteAsync("issues", JsonSerializer.SerializeToElement(new List<Issue>
        {
            new()
            {
                Id = "mes-today", ProjectCode = "mes", TicketNumber = "QTB311", CaseName = "MES 今日完成",
                WorkflowStatus = "处理完成", WorkflowStatusChangedAtUtc = new DateTimeOffset(today.Date.AddHours(9), offset)
            },
            new()
            {
                Id = "wms-today", ProjectCode = "wms", TicketNumber = "QTB411", CaseName = "WMS 今日处理中",
                WorkflowStatus = "处理中", WorkflowStatusChangedAtUtc = new DateTimeOffset(today.Date.AddHours(10), offset)
            },
            new()
            {
                Id = "wms-yesterday", ProjectCode = "wms", TicketNumber = "QTB412", CaseName = "WMS 昨日完成",
                WorkflowStatus = "处理完成", WorkflowStatusChangedAtUtc = new DateTimeOffset(today.Date.AddDays(-1).AddHours(10), offset)
            }
        }));
        var report = await module.InvokeAsync("daily_report", JsonSerializer.SerializeToElement(new { }), context, CancellationToken.None);
        Assert.Equal("全部项目", report.GetProperty("scope").GetString());
        Assert.Equal(2, report.GetProperty("total").GetInt32());
        Assert.All(report.GetProperty("items").EnumerateArray(), entry => Assert.NotEqual("QTB412", entry.GetProperty("ticketNumber").GetString()));
        Assert.Equal(2, report.GetProperty("byProject").GetArrayLength());
        Assert.Contains(report.GetProperty("items").EnumerateArray().ToList(), entry => entry.GetProperty("projectName").GetString() == "WMS");
        // 指定项目时仍只返回该项目。
        var scoped = await module.InvokeAsync("daily_report", JsonSerializer.SerializeToElement(new { projectCode = "wms" }), context, CancellationToken.None);
        Assert.Equal(1, scoped.GetProperty("total").GetInt32());
        Assert.Equal("QTB411", scoped.GetProperty("items")[0].GetProperty("ticketNumber").GetString());
    }

    [Fact]
    public async Task ListIssuesSupportsSortingAndColumnFilters()
    {
        var module = new ProjectIssueModule();
        var context = new TestContext();
        await context.Storage.WriteAsync("issues", JsonSerializer.SerializeToElement(new List<Issue>
        {
            new() { Id = "a", ProjectCode = "mes", TicketNumber = "QTB-A", CaseName = "工单新增", TestStatus = "已完成", WorkflowStatus = "处理完成", RaisedDate = "2026-09-01" },
            new() { Id = "b", ProjectCode = "mes", TicketNumber = "QTB-B", CaseName = "备注传递", TestStatus = "开发中", WorkflowStatus = "处理中", RaisedDate = "2026-09-03" },
            new() { Id = "c", ProjectCode = "mes", TicketNumber = "QTB-C", CaseName = "质量异常单", TestStatus = "已完成", WorkflowStatus = "未处理", RaisedDate = "2026-09-02" }
        }));
        var sorted = await module.InvokeAsync("list_issues", JsonSerializer.SerializeToElement(new { projectCode = "mes", sortBy = "raisedDate", sortDir = "asc" }), context, CancellationToken.None);
        Assert.Equal("QTB-A", sorted.GetProperty("items")[0].GetProperty("ticketNumber").GetString());
        Assert.Equal("QTB-B", sorted.GetProperty("items")[2].GetProperty("ticketNumber").GetString());
        var filtered = await module.InvokeAsync("list_issues", JsonSerializer.SerializeToElement(new
        {
            projectCode = "mes",
            filters = new { testStatus = new[] { "已完成" }, workflowStatus = "处理完成" }
        }), context, CancellationToken.None);
        Assert.Equal(1, filtered.GetProperty("total").GetInt32());
        Assert.Equal("QTB-A", filtered.GetProperty("items")[0].GetProperty("ticketNumber").GetString());
    }

    [Fact]
    public async Task UpdateIssueCreatesTicketWhenTicketNumberUnknown()
    {
        var module = new ProjectIssueModule();
        var context = new TestContext();
        var create = JsonSerializer.SerializeToElement(new { projectCode = "mes", ticketNumber = "QTB900", caseName = "导入前补录", workflowStatus = "处理中" });
        var issue = await module.InvokeAsync("update_issue", create, context, CancellationToken.None);
        Assert.Equal("QTB900", issue.GetProperty("ticketNumber").GetString());
        Assert.Equal("处理中", issue.GetProperty("workflowStatus").GetString());
        Assert.NotEqual(JsonValueKind.Null, issue.GetProperty("workflowStatusChangedAtUtc").ValueKind);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => module.InvokeAsync("set_workflow_status",
            JsonSerializer.SerializeToElement(new { projectCode = "mes", ticketNumber = "QTB404", workflowStatus = "处理完成" }), context, CancellationToken.None));
    }

    private static async Task<JsonElement> ReadSingleAsync(ProjectIssueModule module, TestContext context)
    {
        var list = await module.InvokeAsync("list_issues", JsonSerializer.SerializeToElement(new { projectCode = "mes" }), context, CancellationToken.None);
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        return list.GetProperty("items")[0];
    }

    private sealed class TestContext : IGatewayExtensionContext
    {
        public string Actor => "tester";
        public IGatewayExtensionDatabase Database { get; } = new TestDatabase();
        public IGatewayExtensionLogs Logs => throw new NotSupportedException();
        public IGatewayExtensionMonitoring Monitoring => throw new NotSupportedException();
        public IGatewayExtensionStorage Storage { get; } = new TestStorage();
    }

    private sealed class TestDatabase : IGatewayExtensionDatabase
    {
        public Task<IReadOnlyList<GatewayExtensionProject>> ListProjectsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GatewayExtensionProject>>([new GatewayExtensionProject("mes", "MES", []), new GatewayExtensionProject("wms", "WMS", [])]);
        public Task<GatewayExtensionQueryResult> QueryAsync(string projectCode, string dataSourceKey, string sql, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class TestStorage : IGatewayExtensionStorage
    {
        private readonly Dictionary<string, JsonElement> _items = new();
        public Task<JsonElement?> ReadAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_items.TryGetValue(key, out var value) ? (JsonElement?)value.Clone() : null);
        public Task WriteAsync(string key, JsonElement value, CancellationToken cancellationToken = default)
        {
            _items[key] = value.Clone();
            return Task.CompletedTask;
        }
    }
}
