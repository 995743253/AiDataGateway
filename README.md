# AiDataGateway

面向本地 AI 编程助手的 Windows 数据库访问管控工具。WinForms 负责桌面生命周期，进程内 Kestrel 提供 OAuth2/OIDC、WebAPI 和静态管理页面，WebView2 承载 Vue 3 + Element Plus。

## 当前能力

- ASP.NET Core Identity 用户、密码、角色、锁定和会话管理。
- OpenIddict OAuth2/OIDC 服务，支持 Authorization Code + PKCE、Client Credentials 和 Refresh Token。
- AI 客户端默认只能获得数据源读取、只读查询和提交变更 Scope。
- EF Core + SQLite 保存本地配置、用户、OAuth 客户端、审批和审计。
- Data Protection 密钥由当前 Windows 用户的 DPAPI 保护。
- FreeSql 动态支持 SQL Server、MySQL、PostgreSQL 和 SQLite 数据源。
- SQL 只读分类、多语句拦截、无条件 UPDATE/DELETE 拦截和危险 DDL 默认拒绝。
- 写操作进入本地审批，由 Administrator/Approver 批准后执行。
- WinForms 系统托盘、WebView2 内嵌管理页面。

## 项目结构

```text
src/
├── AiDataGateway.Domain          # 纯领域实体和枚举
├── AiDataGateway.Application     # 用例、接口、SQL 策略，不依赖基础设施
├── AiDataGateway.Infrastructure  # EF Core、Identity、OpenIddict 存储、FreeSql、DPAPI
├── AiDataGateway.Api             # 本地 Kestrel、OAuth2 和 HTTP 端点
├── AiDataGateway.Desktop         # WinForms/WebView2/托盘，仅负责宿主生命周期
└── AiDataGateway.Web             # Vue 3 + Element Plus
tests/
└── AiDataGateway.Tests           # 领域、SQL 策略和完整认证链路测试
```

`Application` 不引用 `Infrastructure` 或桌面项目，因此后续可直接将 API 拆到 Windows Service、IIS 或独立进程。

## 使用文档

- [小白使用说明：客户端操作与 AI 接入](Doc/小白使用说明-AI接入.md)
- [AiDataGateway 使用说明](Doc/AiDataGateway使用说明.md)
- [AI 客户端 API 文档](Doc/AI客户端API文档.md)

## 构建

```powershell
cd src/AiDataGateway.Web
npm install
npm run build

cd ../..
dotnet restore AiDataGateway.slnx --configfile NuGet.Config
dotnet build AiDataGateway.slnx --no-restore
dotnet test AiDataGateway.slnx --no-build --no-restore
```

## 启动

```powershell
dotnet run --project src/AiDataGateway.Desktop/AiDataGateway.Desktop.csproj
```

程序默认监听 `http://127.0.0.1:5127`。首次打开会要求创建管理员，并返回一组只显示一次的 AI OAuth2 `client_id` 和 `client_secret`。

关闭主窗口时程序最小化到系统托盘；必须从托盘选择“退出”才会停止本地 API。

## AI 获取 Token

```http
POST http://127.0.0.1:5127/connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=client_credentials&
client_id=<client-id>&
client_secret=<client-secret>&
scope=gateway.datasource.read gateway.query.execute gateway.change.submit
```

业务请求携带：

```http
Authorization: Bearer <access-token>
```

## 本地数据

默认存放在：

```text
%LocalAppData%\AiDataGateway\
├── gateway.db
├── keys\
├── logs\
└── WebView2\
```

数据库密码不会返回给前端或 AI，存储前由 ASP.NET Core Data Protection 加密；密钥环再由 Windows DPAPI 保护。

## 说明

- 前端采用 Vue 3 + Element Plus，并由 Vite 构建后嵌入桌面程序。
- 当前本地监听为 HTTP，并仅绑定回环地址。若改为局域网或外网监听，必须增加 HTTPS 和来源限制。
- 本地配置库首版使用 `EnsureCreated` 初始化；正式发布数据库升级功能前，应切换为 EF Core migrations。
