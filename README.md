# AiDataGateway

面向本地 AI 编程助手的 Windows 数据库访问管控工具。WPF 负责桌面生命周期，进程内 Kestrel 提供 OAuth2/OIDC、WebAPI 和静态管理页面，WebView2 承载 Vue 3 + Element Plus。

## 当前能力

- ASP.NET Core Identity 用户、密码、角色、锁定和会话管理；支持编辑、启用/禁用和受限永久删除。
- OpenIddict OAuth2/OIDC 服务，支持 Authorization Code + PKCE、Client Credentials 和 Refresh Token。
- OAuth2 客户端支持编辑名称/Scope 和吊销删除；权限调整后旧 Token 立即失效，Client Secret 保持不变。
- AI 客户端默认只能获得项目/数据源读取、只读查询、日志读取、服务器指标读取和提交变更 Scope。
- EF Core + SQLite 保存本地配置、用户、OAuth 客户端、审批和审计。
- Data Protection 密钥由当前 Windows 用户的 DPAPI 保护。
- FreeSql 动态支持 SQL Server、MySQL、PostgreSQL、SQLite、Oracle、MariaDB、达梦 DM8 和 Firebird 数据源。
- 内置 OAuth2 保护的 MCP Streamable HTTP Server（`/mcp`），支持项目解析、数据库只读查询、NLog/Seq 日志查询、服务器指标查询、提交审批和查询工单状态。
- 项目与数据源、日志源、监控节点均为多对多关系；AI 可通过唯一项目编号解析对应资源标识。
- 服务器监控内置 25 项可选指标，支持节点级采集配置、近期实时趋势及按时间范围查询历史趋势。
- 日志源支持本机 NLog、Seq 和远程采集 Agent；自动识别 UTF-8/GB18030，支持 NLog 变量、日期限流、Seq 简易查询与 SSE 实时日志。
- NLog 容错解析支持多行消息/异常、空字段、无结束标记、JSON 多行和末尾未闭合记录。
- 每个数据源可配置独立表黑名单，命中的只读 SQL 会在建立数据库连接前被强制拦截并写入审计日志。
- SQL 只读分类、多语句拦截、无条件 UPDATE/DELETE 拦截和危险 DDL 默认拒绝。
- 写操作进入本地审批，由 Administrator/Approver 批准后执行。
- 独立审批历史与完整 SQL 详情页，保留审批人、意见、执行状态和错误。
- 应用日志页面统一查看结构化事件并可直接搜索加载；独立“实时日志”页通过 SSE 查看新增日志；网关审计记录 AI 查询、日志读取、审批、配置和认证操作。
- 内置本机 CPU、内存、磁盘、网络和运行时间采集；远端服务器使用独立轻量 Agent 与节点级上报密钥。
- 系统设置可配置新工单审批有效期；同时支持按天保留审批记录、审计日志和本地日志文件，默认保留 3 天。
- Windows 客户端支持可开关的内存使用悬浮球，实时显示使用率和容量，可拖动、置顶并记住桌面位置。
- SSE 服务端事件驱动管理表格自动刷新，无定时轮询。
- WPF 无边框桌面壳、系统托盘、WebView2 内嵌管理页面；管理页面支持亮色/暗色主题切换。

## 项目结构

```text
src/
├── AiDataGateway.Domain          # 纯领域实体和枚举
├── AiDataGateway.Application     # 用例、接口、SQL 策略，不依赖基础设施
├── AiDataGateway.Infrastructure  # EF Core、Identity、OpenIddict 存储、FreeSql、DPAPI
├── AiDataGateway.Monitoring      # 无基础设施依赖的跨平台系统指标采集库
├── AiDataGateway.MonitorAgent    # 可单独打包的远端指标与本地日志采集程序
├── AiDataGateway.Api             # 本地 Kestrel、OAuth2 和 HTTP 端点
├── AiDataGateway.Desktop         # WPF/WebView2/托盘，仅负责宿主生命周期
└── AiDataGateway.Web             # Vue 3 + Element Plus
tests/
└── AiDataGateway.Tests           # 领域、SQL 策略和完整认证链路测试
```

`Application` 不引用 `Infrastructure` 或桌面项目，因此后续可直接将 API 拆到 Windows Service、IIS 或独立进程。

## 使用文档

- [小白使用说明：客户端操作与 AI 接入](Doc/小白使用说明-AI接入.md)
- [AiDataGateway 使用说明](Doc/AiDataGateway使用说明.md)
- [AI 客户端 API 文档](Doc/AI客户端API文档.md)
- [MCP Server 接入说明](Doc/MCP服务器接入说明.md)
- [项目与日志接入说明](Doc/项目与日志接入说明.md)
- [服务器监控使用说明](Doc/服务器监控使用说明.md)

## 构建

```powershell
cd src/AiDataGateway.Web
npm install
npm run build

cd ../..
dotnet restore AiDataGateway.sln
dotnet build AiDataGateway.sln --no-restore
dotnet test AiDataGateway.sln --no-build --no-restore
```

## 启动

```powershell
dotnet run --project src/AiDataGateway.Desktop/AiDataGateway.Desktop.csproj
```

程序默认监听 `http://127.0.0.1:5127`。首次打开会要求创建管理员，并返回一组只显示一次的 AI OAuth2 `client_id` 和 `client_secret`。

需要接收远端监控 Agent 上报时，可设置用户环境变量 `AI_GATEWAY_LISTEN_ADDRESS=0.0.0.0` 后重启，并在防火墙中仅向可信内网开放 5127；详细步骤见 [服务器监控使用说明](Doc/服务器监控使用说明.md)。

数据默认保存到 `%LocalAppData%\AiDataGateway`。如需便携运行，可将发布目录中的 `gateway.host.example.json` 复制为 `gateway.host.json`，并将 `storagePath` 设置为 `data`（程序目录下的 `data` 子目录）或 `.`（程序目录本身）。环境变量 `AI_GATEWAY_STORAGE_PATH` 的优先级高于配置文件。详细迁移步骤见 [运行库与数据保存说明](Doc/运行库与数据保存说明.md)。

普通用户推荐使用 GitHub Release 中的 Windows Setup。首次安装可以分别选择程序目录和数据库目录；后续安装器与应用内更新会自动识别原目录，并保留 `gateway.db`、`keys` 和本地日志。发布、更新和卸载行为见 [安装与自动更新说明](Doc/安装与自动更新说明.md)。

关闭主窗口时程序最小化到系统托盘；必须从托盘选择“退出”才会停止本地 API。

## AI 获取 Token

```http
POST http://127.0.0.1:5127/connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=client_credentials&
client_id=<client-id>&
client_secret=<client-secret>&
scope=gateway.datasource.read gateway.query.execute gateway.change.submit gateway.logs.read gateway.metrics.read
```

业务请求携带：

```http
Authorization: Bearer <access-token>
```

支持 MCP 的客户端可连接 `http://127.0.0.1:5127/mcp`，并使用同一个 OAuth2 Bearer Token。详见 [MCP Server 接入说明](Doc/MCP服务器接入说明.md)。

## 本地数据

默认存放在：

```text
%LocalAppData%\AiDataGateway\
├── gateway.db
├── keys\
└── logs\
```

WebView2 缓存仍独立保存在 `%LocalAppData%\AiDataGateway\WebView2`，不属于业务配置数据库。

数据库密码不会返回给前端或 AI，存储前由 ASP.NET Core Data Protection 加密；密钥环再由 Windows DPAPI 保护。

## 说明

- 前端采用 Vue 3 + Element Plus，并由 Vite 构建后嵌入桌面程序。
- 当前本地监听为 HTTP，并仅绑定回环地址。若改为局域网或外网监听，必须增加 HTTPS 和来源限制。
- 本地配置库首版使用 `EnsureCreated` 初始化；正式发布数据库升级功能前，应切换为 EF Core migrations。
