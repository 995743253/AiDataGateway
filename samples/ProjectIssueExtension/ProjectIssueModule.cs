using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using AiDataGateway.Extensions;

namespace ProjectIssueExtension;

public sealed class ProjectIssueModule : IGatewayExtension
{
    private const int MaximumImportBytes = 5 * 1024 * 1024;
    private const int MaximumIssues = 20_000;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly JsonElement ListSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object", properties = new {
            projectCode = StringField("项目编号"), keyword = StringField("单号、个案或程序关键词"), status = StringField("测试状态"), workflowStatus = StringField("变动状态（如 未处理/处理中/处理完成）"),
            sortBy = StringField("排序字段：ticketNumber/testStatus/workflowStatus/resolved/category/caseName/programName/owner/developer/raisedDate/completedDate/updatedAtUtc/changedAtUtc"),
            sortDir = StringField("排序方向 asc/desc，默认 updatedAtUtc 倒序"),
            filters = new { type = "object", description = "按列筛选：值为字符串按包含匹配，值为字符串数组按精确匹配任一", additionalProperties = true },
            page = new { type = "integer", minimum = 1 }, pageSize = new { type = "integer", minimum = 1, maximum = 200 }
        },
        required = new[] { "projectCode" }, additionalProperties = false
    });
    private static readonly JsonElement IdSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object", properties = new { projectCode = StringField("项目编号"), id = StringField("记录 ID") },
        required = new[] { "projectCode", "id" }, additionalProperties = false
    });
    private static readonly JsonElement SaveSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object", properties = new { projectCode = StringField("项目编号"), issue = new { type = "object", description = "问题单字段" } },
        required = new[] { "projectCode", "issue" }, additionalProperties = false
    });
    private static readonly JsonElement ImportSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object", properties = new { projectCode = StringField("项目编号"), fileName = StringField("文件名，支持 xlsx/csv/tsv"), base64 = StringField("文件 Base64，可选"), text = StringField("从在线表格复制的制表符或 CSV 文本，可选"), sourceUrl = StringField("在线文档源链接，仅作溯源") },
        required = new[] { "projectCode" }, additionalProperties = false
    });
    private static readonly JsonElement UpdateSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new {
            projectCode = StringField("项目编号"), ticketNumber = StringField("单号定位（与 id 二选一）"), id = StringField("记录 ID 定位（与 ticketNumber 二选一）"),
            testStatus = StringField("测试状态"), resolved = StringField("是否解决"), solutionNote = StringField("解决描述"), workflowStatus = StringField("变动状态：未处理/处理中/处理完成 等；变化时自动记录变动时间"),
            category = StringField("类别"), caseName = StringField("个案书名"), programName = StringField("程序名称"), description = StringField("问题描述"),
            handler = StringField("问题处理人"), developmentNote = StringField("开发说明"), owner = StringField("负责顾问"), developer = StringField("开发人员"), remark = StringField("备注")
        },
        required = new[] { "projectCode" }, additionalProperties = false
    });
    private static readonly JsonElement WorkflowSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object", properties = new { projectCode = StringField("项目编号"), ticketNumber = StringField("单号定位（与 id 二选一）"), id = StringField("记录 ID 定位"), workflowStatus = StringField("变动状态：未处理/处理中/处理完成 等") },
        required = new[] { "projectCode", "workflowStatus" }, additionalProperties = false
    });
    private static readonly JsonElement DailySchema = JsonSerializer.SerializeToElement(new
    {
        type = "object", properties = new { projectCode = StringField("项目编号，留空则汇总所有启用项目"), date = StringField("yyyy-MM-dd，默认当天（按服务器本地时区）") },
        additionalProperties = false
    });

    public GatewayExtensionDefinition Definition { get; } = new(
        "project-issue-tracker", "项目问题单", "1.3.0",
        "按网关项目管理问题与 Q 单号；支持 Excel/CSV、在线表格复制粘贴和人工维护；AI 可按单号维护单据、变动变动状态并生成当天变动日报。",
        "项目问题单", "wwwroot/index.html",
        [
            new("list_projects", "列出可管理问题单的网关项目。", Schema(new { }), GatewayExtensionCapability.DataSourceRead),
            new("list_issues", "按项目、状态、变动状态和关键词查询问题与 Q 单号。", ListSchema, GatewayExtensionCapability.DataSourceRead),
            new("get_issue", "读取问题单的完整字段、变动状态历史与来源。", IdSchema, GatewayExtensionCapability.DataSourceRead),
            new("summarize_issues", "汇总指定项目的问题单总数、测试状态、变动状态、类别及解决情况。", JsonSerializer.SerializeToElement(new { type = "object", properties = new { projectCode = StringField("项目编号") }, required = new[] { "projectCode" }, additionalProperties = false }), GatewayExtensionCapability.DataSourceRead),
            new("daily_report", "归纳指定日期（默认当天）变动状态发生变化的问题单；projectCode 留空时汇总所有启用项目，用于生成日报。", DailySchema, GatewayExtensionCapability.DataSourceRead),
            new("update_issue", "按单号提交表单维护问题单：更新测试状态、解决描述、变动状态等；单号不存在时自动新建。仅更新传入的字段。", UpdateSchema, GatewayExtensionCapability.DataSourceRead, ReadOnly: false, VisibleInUi: false),
            new("set_workflow_status", "按单号变动问题单的变动状态（如 未处理→处理完成）；状态实际变化时自动记录变动时间，供日报归纳。", WorkflowSchema, GatewayExtensionCapability.DataSourceRead, ReadOnly: false, VisibleInUi: false),
            new("save_issue", "人工新增或更新问题单。", SaveSchema, GatewayExtensionCapability.DataSourceRead, ReadOnly: false, VisibleInMcp: false),
            new("delete_issue", "人工删除问题单。", IdSchema, GatewayExtensionCapability.DataSourceRead, ReadOnly: false, VisibleInMcp: false),
            new("import_issues", "导入 Excel/CSV 或从在线文档复制的表格。", ImportSchema, GatewayExtensionCapability.DataSourceRead, ReadOnly: false, VisibleInMcp: false)
        ]);

    public async Task<JsonElement> InvokeAsync(string operation, JsonElement arguments, IGatewayExtensionContext context, CancellationToken cancellationToken)
    {
        if (operation == "list_projects")
            return JsonSerializer.SerializeToElement(new { items = await context.Database.ListProjectsAsync(cancellationToken) }, Json);

        if (operation == "daily_report")
            return await DailyReportAsync(arguments, context, cancellationToken);

        var projectCode = Required(arguments, "projectCode");
        var projects = await context.Database.ListProjectsAsync(cancellationToken);
        if (!projects.Any(item => string.Equals(item.Code, projectCode, StringComparison.OrdinalIgnoreCase)))
            throw new KeyNotFoundException("项目不存在或未启用。");

        return operation switch
        {
            "list_issues" => await ListAsync(projectCode, arguments, context.Storage, cancellationToken),
            "get_issue" => await GetAsync(projectCode, Required(arguments, "id"), context.Storage, cancellationToken),
            "summarize_issues" => await SummarizeAsync(projectCode, context.Storage, cancellationToken),
            "update_issue" => await UpdateAsync(projectCode, arguments, context, cancellationToken),
            "set_workflow_status" => await SetWorkflowStatusAsync(projectCode, arguments, context, cancellationToken),
            "save_issue" => await SaveAsync(projectCode, arguments, context, cancellationToken),
            "delete_issue" => await DeleteAsync(projectCode, Required(arguments, "id"), context.Storage, cancellationToken),
            "import_issues" => await ImportAsync(projectCode, arguments, context, cancellationToken),
            _ => throw new ArgumentException("未知操作。")
        };
    }

    private static readonly Dictionary<string, Func<Issue, object?>> SortKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ticketNumber"] = item => item.TicketNumber, ["caseName"] = item => item.CaseName, ["programName"] = item => item.ProgramName,
        ["category"] = item => item.Category, ["testStatus"] = item => item.TestStatus, ["resolved"] = item => item.Resolved,
        ["workflowStatus"] = item => item.WorkflowStatus, ["owner"] = item => item.Owner, ["developer"] = item => item.Developer,
        ["handler"] = item => item.Handler, ["raisedDate"] = item => item.RaisedDate, ["completedDate"] = item => item.CompletedDate,
        ["description"] = item => item.Description, ["solutionNote"] = item => item.SolutionNote,
        ["updatedAtUtc"] = item => item.UpdatedAtUtc, ["createdAtUtc"] = item => item.CreatedAtUtc, ["changedAtUtc"] = item => item.WorkflowStatusChangedAtUtc
    };

    private static async Task<JsonElement> ListAsync(string projectCode, JsonElement args, IGatewayExtensionStorage storage, CancellationToken ct)
    {
        var all = await ReadAsync(storage, ct);
        var keyword = Optional(args, "keyword")?.Trim();
        var status = Optional(args, "status")?.Trim();
        var workflow = Optional(args, "workflowStatus")?.Trim();
        var page = Math.Max(1, Number(args, "page", 1));
        var size = Math.Clamp(Number(args, "pageSize", 30), 1, 200);
        IEnumerable<Issue> query = all.Where(item => Same(item.ProjectCode, projectCode));
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(item => Same(item.TestStatus, status));
        if (workflow is { Length: > 0 }) query = workflow == "-" ? query.Where(item => string.IsNullOrWhiteSpace(item.WorkflowStatus)) : query.Where(item => Same(item.WorkflowStatus, workflow));
        if (!string.IsNullOrWhiteSpace(keyword)) query = query.Where(item =>
            new[] { item.TicketNumber, item.CaseName, item.ProgramName, item.Description, item.DevelopmentNote, item.Handler, item.Category, item.SolutionNote }
                .Any(value => value?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true));
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty("filters", out var filtersElement) && filtersElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in filtersElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    var text = property.Value.GetString();
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    query = query.Where(item => FieldMatches(item, property.Name, text, exact: false));
                }
                else if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    var values = property.Value.EnumerateArray()
                        .Where(item => item.ValueKind == JsonValueKind.String)
                        .Select(item => item.GetString()?.Trim())
                        .Where(item => !string.IsNullOrEmpty(item)).ToArray();
                    if (values.Length == 0) continue;
                    query = query.Where(item => values.Any(value => FieldMatches(item, property.Name, value!, exact: true)));
                }
            }
        }
        var sortBy = Optional(args, "sortBy")?.Trim();
        var descending = !string.Equals(Optional(args, "sortDir")?.Trim(), "asc", StringComparison.OrdinalIgnoreCase);
        var ordered = sortBy is { Length: > 0 } && SortKeys.TryGetValue(sortBy, out var sortKey)
            ? (descending ? query.OrderByDescending(sortKey).ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                          : query.OrderBy(sortKey).ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
            : query.OrderByDescending(item => item.UpdatedAtUtc).ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var listed = ordered.ToArray();
        return JsonSerializer.SerializeToElement(new { total = listed.Length, page, pageSize = size, items = listed.Skip((page - 1) * size).Take(size) }, Json);
    }

    private static bool FieldMatches(Issue item, string field, string value, bool exact)
    {
        if (!SortKeys.TryGetValue(field, out var key)) return true;
        var actual = key(item)?.ToString();
        if (string.IsNullOrWhiteSpace(actual)) return false;
        return exact ? string.Equals(actual.Trim(), value.Trim(), StringComparison.OrdinalIgnoreCase)
                     : actual.Contains(value.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<JsonElement> GetAsync(string projectCode, string id, IGatewayExtensionStorage storage, CancellationToken ct)
    {
        var issue = (await ReadAsync(storage, ct)).FirstOrDefault(item => Same(item.ProjectCode, projectCode) && Same(item.Id, id))
            ?? throw new KeyNotFoundException("问题单不存在。");
        return JsonSerializer.SerializeToElement(issue, Json);
    }

    private static async Task<JsonElement> SummarizeAsync(string projectCode, IGatewayExtensionStorage storage, CancellationToken ct)
    {
        var issues = (await ReadAsync(storage, ct)).Where(item => Same(item.ProjectCode, projectCode)).ToArray();
        static SummaryBucket[] Group(IEnumerable<string> values) => values.GroupBy(value => string.IsNullOrWhiteSpace(value) ? "未标记" : value.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => new SummaryBucket(group.Key, group.Count())).OrderByDescending(item => item.Count).ToArray();
        return JsonSerializer.SerializeToElement(new
        {
            total = issues.Length,
            withTicket = issues.Count(item => !string.IsNullOrWhiteSpace(item.TicketNumber)),
            statuses = Group(issues.Select(item => item.TestStatus)),
            workflowStatuses = Group(issues.Select(item => item.WorkflowStatus)),
            categories = Group(issues.Select(item => item.Category)),
            resolution = Group(issues.Select(item => item.Resolved))
        }, Json);
    }

    private static async Task<JsonElement> DailyReportAsync(JsonElement args, IGatewayExtensionContext context, CancellationToken ct)
    {
        var nowLocal = DateTimeOffset.Now;
        var (startLocal, dayText) = ParseDay(Optional(args, "date")) ?? (nowLocal.Date, nowLocal.Date.ToString("yyyy-MM-dd"));
        var offset = TimeZoneInfo.Local.GetUtcOffset(startLocal);
        var startUtc = new DateTimeOffset(startLocal, offset);
        var endUtc = startUtc.AddDays(1);
        var projectCode = Optional(args, "projectCode")?.Trim() ?? "";
        var projects = await context.Database.ListProjectsAsync(ct);
        if (projectCode.Length > 0 && !projects.Any(item => Same(item.Code, projectCode)))
            throw new KeyNotFoundException("项目不存在或未启用。");
        string ProjectName(string code) => projects.FirstOrDefault(item => Same(item.Code, code))?.Name ?? code;
        var changed = (await ReadAsync(context.Storage, ct))
            .Where(item => projectCode.Length == 0 || Same(item.ProjectCode, projectCode))
            .Where(item => item.WorkflowStatusChangedAtUtc is { } changedAt && changedAt >= startUtc && changedAt < endUtc)
            .OrderBy(item => item.WorkflowStatusChangedAtUtc)
            .ToArray();
        var byStatus = changed.GroupBy(item => string.IsNullOrWhiteSpace(item.WorkflowStatus) ? "未标记" : item.WorkflowStatus.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => new SummaryBucket(group.Key, group.Count())).OrderByDescending(item => item.Count).ToArray();
        var byProject = changed.GroupBy(item => item.ProjectCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => new SummaryBucket(ProjectName(group.Key), group.Count())).OrderByDescending(item => item.Count).ToArray();
        var items = changed.Select(item => new
        {
            item.ProjectCode, projectName = ProjectName(item.ProjectCode),
            item.Id, item.TicketNumber, item.CaseName, item.ProgramName, item.Category,
            item.TestStatus, item.Resolved, workflowStatus = item.WorkflowStatus,
            changedAtLocal = item.WorkflowStatusChangedAtUtc!.Value.ToOffset(offset).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            item.SolutionNote, item.Description, item.RaisedDate, item.CompletedDate
        }).ToArray();
        return JsonSerializer.SerializeToElement(new
        {
            date = dayText, scope = projectCode.Length > 0 ? ProjectName(projectCode) : "全部项目", total = changed.Length, byStatus, byProject, items
        }, Json);
    }

    private static async Task<JsonElement> UpdateAsync(string projectCode, JsonElement args, IGatewayExtensionContext context, CancellationToken ct)
    {
        var ticketNumber = Optional(args, "ticketNumber")?.Trim() ?? "";
        var id = Optional(args, "id")?.Trim() ?? "";
        if (ticketNumber.Length == 0 && id.Length == 0) throw new ArgumentException("请提供 ticketNumber 或 id 定位问题单。");
        var now = DateTimeOffset.UtcNow;
        await Gate.WaitAsync(ct);
        Issue issue;
        try
        {
            var all = await ReadAsync(context.Storage, ct);
            var index = all.FindIndex(item => Same(item.ProjectCode, projectCode) && (id.Length > 0 ? Same(item.Id, id) : Same(item.TicketNumber, ticketNumber)));
            if (index >= 0)
            {
                issue = all[index];
                ApplyFields(issue, args, context.Actor, now);
                issue.UpdatedBy = context.Actor;
                issue.UpdatedAtUtc = now;
                all[index] = issue;
            }
            else
            {
                if (id.Length > 0) throw new KeyNotFoundException("问题单不存在，不能按 id 新建。");
                if (!HasAnyField(args, "caseName", "programName", "description", "testStatus", "resolved", "solutionNote", "workflowStatus",
                        "category", "handler", "developmentNote", "owner", "developer", "remark"))
                    throw new ArgumentException("缺少要维护的字段内容。");
                if (all.Count >= MaximumIssues) throw new InvalidOperationException("问题单数量已达上限。");
                issue = new Issue { Id = Guid.NewGuid().ToString("N"), ProjectCode = projectCode, TicketNumber = ticketNumber, CreatedAtUtc = now };
                ApplyFields(issue, args, context.Actor, now);
                if (issue.TicketNumber.Length == 0 && issue.CaseName.Length == 0 && string.IsNullOrWhiteSpace(issue.Description))
                    throw new ArgumentException("单号、个案书名或问题描述至少填写一项。");
                issue.UpdatedBy = context.Actor;
                issue.UpdatedAtUtc = now;
                all.Add(issue);
            }
            await WriteAsync(context.Storage, all, ct);
        }
        finally { Gate.Release(); }
        return JsonSerializer.SerializeToElement(issue, Json);
    }

    private static async Task<JsonElement> SetWorkflowStatusAsync(string projectCode, JsonElement args, IGatewayExtensionContext context, CancellationToken ct)
    {
        var ticketNumber = Optional(args, "ticketNumber")?.Trim() ?? "";
        var id = Optional(args, "id")?.Trim() ?? "";
        if (ticketNumber.Length == 0 && id.Length == 0) throw new ArgumentException("请提供 ticketNumber 或 id 定位问题单。");
        var incoming = Required(args, "workflowStatus");
        var now = DateTimeOffset.UtcNow;
        await Gate.WaitAsync(ct);
        Issue issue;
        var before = "";
        try
        {
            var all = await ReadAsync(context.Storage, ct);
            var index = all.FindIndex(item => Same(item.ProjectCode, projectCode) && (id.Length > 0 ? Same(item.Id, id) : Same(item.TicketNumber, ticketNumber)));
            if (index < 0) throw new KeyNotFoundException($"问题单不存在：{(ticketNumber.Length > 0 ? ticketNumber : id)}。");
            issue = all[index];
            before = issue.WorkflowStatus;
            ApplyWorkflow(issue, incoming, context.Actor, now);
            issue.UpdatedBy = context.Actor;
            issue.UpdatedAtUtc = now;
            all[index] = issue;
            await WriteAsync(context.Storage, all, ct);
        }
        finally { Gate.Release(); }
        var changed = !Same(before, issue.WorkflowStatus);
        return JsonSerializer.SerializeToElement(new
        {
            issue.Id, issue.TicketNumber, workflowStatus = issue.WorkflowStatus,
            workflowStatusChangedAtUtc = issue.WorkflowStatusChangedAtUtc, changed,
            message = changed ? "变动状态已更新并记录变动时间。" : "状态未变化，变动时间保持不变。"
        }, Json);
    }

    private static async Task<JsonElement> SaveAsync(string projectCode, JsonElement args, IGatewayExtensionContext context, CancellationToken ct)
    {
        if (!args.TryGetProperty("issue", out var data) || data.ValueKind != JsonValueKind.Object) throw new ArgumentException("缺少问题单内容。");
        var issue = JsonSerializer.Deserialize<Issue>(data.GetRawText(), Json) ?? throw new ArgumentException("问题单格式不正确。");
        issue.ProjectCode = projectCode;
        issue.TicketNumber = issue.TicketNumber?.Trim() ?? "";
        issue.CaseName = issue.CaseName?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(issue.SourceUrl) &&
            (!Uri.TryCreate(issue.SourceUrl, UriKind.Absolute, out var sourceUri) || sourceUri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("来源链接必须是 HTTPS 地址。");
        if (issue.TicketNumber.Length == 0 && issue.CaseName.Length == 0 && string.IsNullOrWhiteSpace(issue.Description))
            throw new ArgumentException("单号、个案书名或问题描述至少填写一项。");
        var incomingWorkflow = issue.WorkflowStatus;
        issue.WorkflowStatus = "";
        issue.WorkflowStatusChangedAtUtc = null;
        issue.WorkflowHistory = [];
        issue.UpdatedBy = context.Actor;
        issue.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await Gate.WaitAsync(ct);
        try
        {
            var all = await ReadAsync(context.Storage, ct);
            var existing = all.FindIndex(item => Same(item.ProjectCode, projectCode) && Same(item.Id, issue.Id));
            if (existing >= 0)
            {
                issue.Id = all[existing].Id;
                issue.CreatedAtUtc = all[existing].CreatedAtUtc;
                // 表单更新沿用系统内既有变动状态基线，状态不变时变动时间保持不变。
                issue.WorkflowStatus = all[existing].WorkflowStatus;
                issue.WorkflowStatusChangedAtUtc = all[existing].WorkflowStatusChangedAtUtc;
                issue.WorkflowHistory = all[existing].WorkflowHistory;
                all[existing] = issue;
            }
            else
            {
                if (all.Count >= MaximumIssues) throw new InvalidOperationException("问题单数量已达上限。");
                issue.Id = Guid.NewGuid().ToString("N");
                issue.CreatedAtUtc = issue.UpdatedAtUtc;
                all.Add(issue);
            }
            ApplyWorkflow(issue, incomingWorkflow, context.Actor, issue.UpdatedAtUtc);
            await WriteAsync(context.Storage, all, ct);
        }
        finally { Gate.Release(); }
        return JsonSerializer.SerializeToElement(issue, Json);
    }

    private static async Task<JsonElement> DeleteAsync(string projectCode, string id, IGatewayExtensionStorage storage, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var all = await ReadAsync(storage, ct);
            var removed = all.RemoveAll(item => Same(item.ProjectCode, projectCode) && Same(item.Id, id));
            if (removed == 0) throw new KeyNotFoundException("问题单不存在。");
            await WriteAsync(storage, all, ct);
            return JsonSerializer.SerializeToElement(new { deleted = removed }, Json);
        }
        finally { Gate.Release(); }
    }

    private static async Task<JsonElement> ImportAsync(string projectCode, JsonElement args, IGatewayExtensionContext context, CancellationToken ct)
    {
        var fileName = Optional(args, "fileName") ?? "在线文档粘贴.tsv";
        var sourceUrl = Optional(args, "sourceUrl")?.Trim() ?? "";
        if (sourceUrl.Length > 0 && (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("来源链接必须是 HTTPS 地址。");
        var base64 = Optional(args, "base64");
        var text = Optional(args, "text");
        List<(string Sheet, int Row, Dictionary<string, string> Cells)> rows;
        if (!string.IsNullOrWhiteSpace(base64))
        {
            if (base64.Length > MaximumImportBytes * 4 / 3 + 100) throw new ArgumentException("导入文件超过 5 MB。");
            var bytes = Convert.FromBase64String(base64);
            if (bytes.Length > MaximumImportBytes) throw new ArgumentException("导入文件超过 5 MB。");
            rows = Path.GetExtension(fileName).ToLowerInvariant() switch
            {
                ".xlsx" => IssueSpreadsheetReader.ReadXlsx(bytes),
                ".csv" => IssueSpreadsheetReader.ReadText(Encoding.UTF8.GetString(bytes), fileName),
                ".tsv" or ".txt" => IssueSpreadsheetReader.ReadText(Encoding.UTF8.GetString(bytes), fileName),
                _ => throw new ArgumentException("仅支持 .xlsx、.csv、.tsv 文件。")
            };
        }
        else
        {
            if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("请选择文件或粘贴在线表格内容。");
            if (Encoding.UTF8.GetByteCount(text) > MaximumImportBytes) throw new ArgumentException("粘贴内容超过 5 MB。");
            rows = IssueSpreadsheetReader.ReadText(text, fileName);
        }
        if (rows.Count > 5000) throw new ArgumentException("单次最多导入 5000 行。");
        var now = DateTimeOffset.UtcNow;
        var incoming = rows.Select(row => IssueSpreadsheetReader.ToIssue(projectCode, row.Cells, fileName, row.Sheet, row.Row, sourceUrl, context.Actor, now))
            .Where(issue => !string.IsNullOrWhiteSpace(issue.TicketNumber) || !string.IsNullOrWhiteSpace(issue.CaseName) || !string.IsNullOrWhiteSpace(issue.Description))
            .ToArray();
        await Gate.WaitAsync(ct);
        int created = 0, updated = 0;
        try
        {
            var all = await ReadAsync(context.Storage, ct);
            foreach (var issue in incoming)
            {
                var index = all.FindIndex(item => Same(item.ProjectCode, projectCode) &&
                    (!string.IsNullOrWhiteSpace(issue.TicketNumber) && Same(item.TicketNumber, issue.TicketNumber) ||
                     string.IsNullOrWhiteSpace(issue.TicketNumber) && Same(item.SourceUrl, sourceUrl) && Same(item.SourceFileName, issue.SourceFileName) && Same(item.SourceSheet, issue.SourceSheet) && item.SourceRow == issue.SourceRow));
                var importedWorkflow = issue.WorkflowStatus;
                issue.WorkflowStatus = "";
                if (index >= 0)
                {
                    issue.Id = all[index].Id;
                    issue.CreatedAtUtc = all[index].CreatedAtUtc;
                    // 导入不携带变动状态列时保留系统内已有状态，避免回导覆盖。
                    if (string.IsNullOrWhiteSpace(importedWorkflow))
                    {
                        issue.WorkflowStatus = all[index].WorkflowStatus;
                        issue.WorkflowStatusChangedAtUtc = all[index].WorkflowStatusChangedAtUtc;
                        issue.WorkflowHistory = all[index].WorkflowHistory;
                    }
                    all[index] = issue;
                    if (!string.IsNullOrWhiteSpace(importedWorkflow)) ApplyWorkflow(issue, importedWorkflow, context.Actor, now);
                    updated++;
                }
                else
                {
                    if (all.Count >= MaximumIssues) throw new InvalidOperationException("问题单数量已达上限。");
                    all.Add(issue);
                    if (!string.IsNullOrWhiteSpace(importedWorkflow)) ApplyWorkflow(issue, importedWorkflow, context.Actor, now);
                    created++;
                }
            }
            await WriteAsync(context.Storage, all, ct);
        }
        finally { Gate.Release(); }
        return JsonSerializer.SerializeToElement(new { parsed = incoming.Length, created, updated, skipped = rows.Count - incoming.Length }, Json);
    }

    /// <summary>变动状态仅在值实际变化时刷新变动时间；人工表单和 AI 工具共用该语义。</summary>
    private static void ApplyWorkflow(Issue issue, string? incoming, string actor, DateTimeOffset now)
    {
        incoming = incoming?.Trim() ?? "";
        if (Same(issue.WorkflowStatus, incoming)) return;
        issue.WorkflowHistory.Add(new WorkflowTransition(issue.WorkflowStatus, incoming, now, actor));
        issue.WorkflowStatus = incoming;
        issue.WorkflowStatusChangedAtUtc = now;
    }

    private static void ApplyFields(Issue issue, JsonElement args, string actor, DateTimeOffset now)
    {
        if (TryOptional(args, "caseName", out var value)) issue.CaseName = value;
        if (TryOptional(args, "programName", out value)) issue.ProgramName = value;
        if (TryOptional(args, "description", out value)) issue.Description = value;
        if (TryOptional(args, "testStatus", out value)) issue.TestStatus = value;
        if (TryOptional(args, "resolved", out value)) issue.Resolved = value;
        if (TryOptional(args, "solutionNote", out value)) issue.SolutionNote = value;
        if (TryOptional(args, "category", out value)) issue.Category = value;
        if (TryOptional(args, "handler", out value)) issue.Handler = value;
        if (TryOptional(args, "developmentNote", out value)) issue.DevelopmentNote = value;
        if (TryOptional(args, "owner", out value)) issue.Owner = value;
        if (TryOptional(args, "developer", out value)) issue.Developer = value;
        if (TryOptional(args, "remark", out value)) issue.Remark = value;
        if (TryOptional(args, "workflowStatus", out value)) ApplyWorkflow(issue, value, actor, now);
    }

    private static async Task<List<Issue>> ReadAsync(IGatewayExtensionStorage storage, CancellationToken ct)
    {
        var value = await storage.ReadAsync("issues", ct);
        return value.HasValue ? JsonSerializer.Deserialize<List<Issue>>(value.Value.GetRawText(), Json) ?? [] : [];
    }

    private static Task WriteAsync(IGatewayExtensionStorage storage, List<Issue> issues, CancellationToken ct) =>
        storage.WriteAsync("issues", JsonSerializer.SerializeToElement(issues, Json), ct);

    private static (DateTime Local, string Text)? ParseDay(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!DateTime.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            throw new ArgumentException("date 需要 yyyy-MM-dd 格式。");
        return (day, day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    private static bool HasAnyField(JsonElement args, params string[] names) => names.Any(name => args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out _));
    private static bool Same(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
    private static string Required(JsonElement args, string name) => Optional(args, name) is { Length: > 0 } value ? value.Trim() : throw new ArgumentException($"缺少 {name}。");
    private static string? Optional(JsonElement args, string name) => args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool TryOptional(JsonElement args, string name, out string value)
    {
        value = "";
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String) return false;
        value = element.GetString()?.Trim() ?? "";
        return true;
    }
    private static int Number(JsonElement args, string name, int fallback) => args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : fallback;
    private static object StringField(string description) => new { type = "string", description };
    private static JsonElement Schema(object properties) => JsonSerializer.SerializeToElement(new { type = "object", properties, additionalProperties = false });
    private sealed record SummaryBucket(string Name, int Count);
}

public sealed record WorkflowTransition(string From, string To, DateTimeOffset AtUtc, string By);

public sealed class Issue
{
    public string Id { get; set; } = "";
    public string ProjectCode { get; set; } = "";
    public string TicketNumber { get; set; } = "";
    public string CaseName { get; set; } = "";
    public string ProgramName { get; set; } = "";
    public string Description { get; set; } = "";
    public string TestStatus { get; set; } = "";
    public string Resolved { get; set; } = "";
    public string Category { get; set; } = "";
    public string Handler { get; set; } = "";
    public string DevelopmentNote { get; set; } = "";
    public string Owner { get; set; } = "";
    public string Developer { get; set; } = "";
    public string Remark { get; set; } = "";
    public string SolutionNote { get; set; } = "";
    public string WorkflowStatus { get; set; } = "";
    public DateTimeOffset? WorkflowStatusChangedAtUtc { get; set; }
    public List<WorkflowTransition> WorkflowHistory { get; set; } = [];
    public string RaisedDate { get; set; } = "";
    public string CompletedDate { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string SourceFileName { get; set; } = "";
    public string SourceSheet { get; set; } = "";
    public int SourceRow { get; set; }
    public Dictionary<string, string> ExtraFields { get; set; } = new();
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string UpdatedBy { get; set; } = "";
}
