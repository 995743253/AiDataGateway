# 本地 AI 数据库访问管控工具技术方案

## 1. 产品定位

本系统是运行在开发人员 Windows 电脑上的 AI 配套工具，用于管控本地 AI 编程助手访问数据库的行为，而不是面向大型客户部署的企业级网关平台。

AI 不直接持有数据库连接信息，而是通过本机工具提交查询或变更请求。本工具负责选择数据源、检查 SQL、限制权限、执行查询、弹出人工确认并记录审计日志。

核心目标是：

- 单机运行，安装和启动足够轻量。
- 一个 WinForms 程序即可承载后台服务和管理界面。
- 本机同时运行 WebAPI/MCP 服务，供 AI 客户端调用。
- WinForms 使用 WebView2 内嵌 Vue 3 + Element Plus 页面。
- 支持多个数据库连接，数据库可以位于不同 IP、端口和网络环境。
- 支持不同数据库类型，而不只是一套数据库中的多个库。
- 查询默认自动执行，危险操作由本机人工确认。
- IIS、Docker、外部审批系统和复杂 IAM 均不是首期必需能力。

## 2. 推荐技术基线

| 领域 | 推荐方案 |
|---|---|
| 桌面宿主 | C# WinForms |
| 本地服务 | ASP.NET Core Minimal API + Kestrel，随 WinForms 进程启动 |
| AI 接入 | MCP HTTP/SSE 或本地 WebAPI；按 AI 客户端能力适配 |
| 管理界面 | Vue 3 + Element Plus |
| 页面承载 | WebView2，加载本地 WebAPI 提供的前端静态资源 |
| 本地配置库 | SQLite |
| 配置库访问 | EF Core SQLite |
| 用户体系 | ASP.NET Core Identity + EF Core |
| OAuth2/OIDC | OpenIddict Server + Validation |
| Token | 本地 RSA X.509 证书签名的 JWT Access Token |
| 目标数据库访问 | FreeSql 或数据库原生 ADO.NET Provider |
| 密码保护 | Windows DPAPI 或 Windows Credential Manager |
| 日志 | 本地结构化文件 + SQLite 审计索引 |
| 发布方式 | 普通发布目录或自包含单机发布；无需 IIS、Docker |

建议采用“EF Core 管本地数据，FreeSql/ADO.NET 访问目标数据库”的组合：

- EF Core 适合管理本工具自己的数据模型，例如数据源配置、策略、审批记录和审计索引。
- AI 生成的是任意 SQL，目标数据库结构也不固定，因此不适合为每个客户数据库建立固定 EF Core 实体模型。
- FreeSql 或原生 ADO.NET 更适合按运行时配置连接不同数据库，并执行参数化原生 SQL。
- 如果希望减少依赖，也可以统一使用 FreeSql；但仍应将本地配置库与目标数据库连接实例分开管理。

## 3. 轻量总体架构

```text
本地 AI 客户端
    │
    │ MCP / HTTP（默认 127.0.0.1）
    ▼
┌─────────────────────────────────────────────┐
│ AiDataGateway.exe（WinForms 单进程）        │
│                                             │
│  ┌───────────────────────────────────────┐  │
│  │ ASP.NET Core / Kestrel 本地服务       │  │
│  │ - MCP 工具                            │  │
│  │ - WebAPI                              │  │
│  │ - Vue 静态资源                        │  │
│  └───────────────────────────────────────┘  │
│                    │                        │
│  ┌─────────────────▼─────────────────────┐  │
│  │ 管控核心                              │  │
│  │ 数据源路由 / SQL 检查 / 权限 / 审批   │  │
│  │ 连接管理 / 脱敏 / 审计                │  │
│  └─────────────────┬─────────────────────┘  │
│                    │                        │
│  ┌─────────────────▼─────────────────────┐  │
│  │ WinForms + WebView2                   │  │
│  │ 数据源配置 / SQL 预览 / 审批 / 日志   │  │
│  └───────────────────────────────────────┘  │
└────────────────────┬────────────────────────┘
                     │
       ┌─────────────┼──────────────┐
       ▼             ▼              ▼
 SQL Server A     MySQL B      PostgreSQL C
 10.0.1.10        172.16.2.8   公网/VPN 地址
```

首期不拆微服务，不单独部署前端、API 和审批服务。WinForms 是进程宿主，ASP.NET Core 在后台线程中随应用启动；关闭窗口时默认最小化到系统托盘，选择“退出”才停止本地 API 和数据库连接。

## 4. 应用运行方式

### 4.1 启动过程

1. 启动 `AiDataGateway.exe`。
2. 初始化日志、本地 SQLite 和加密配置。
3. 在同一进程内启动 ASP.NET Core Kestrel。
4. 默认监听 `127.0.0.1` 的固定或自动分配端口。
5. WinForms 主窗体初始化 WebView2。
6. WebView2 打开 `http://127.0.0.1:{port}/`，加载 Vue 管理页面。
7. 系统托盘显示运行状态、监听地址、待审批数量和快捷开关。

### 4.2 进程模型

```text
WinForms UI Thread
├── 主窗口
├── WebView2
├── 系统托盘
└── 本地审批提示

.NET Generic Host
├── ASP.NET Core Kestrel
├── MCP/API Endpoint
├── SQL Guard
├── DataSource Registry
├── Connection Manager
├── Approval Queue
└── Audit Writer
```

WinForms 只负责生命周期、托盘和本机交互，核心逻辑放在独立类库中，避免业务代码与窗体事件耦合。

## 5. 建议解决方案结构

```text
src/
├── AiDataGateway.Desktop
│   ├── WinForms 主程序
│   ├── WebView2 宿主
│   ├── 系统托盘
│   └── ASP.NET Core Host 启停
├── AiDataGateway.Api
│   ├── MCP 工具
│   ├── Minimal API
│   ├── OAuth2/OIDC 端点
│   └── Vue 静态资源
├── AiDataGateway.Identity
│   ├── ASP.NET Core Identity
│   ├── OpenIddict Server
│   ├── 用户、角色与客户端管理
│   └── Token 签发、验证与吊销
├── AiDataGateway.Core
│   ├── SQL 请求模型
│   ├── 策略与风险分级
│   ├── 审批状态机
│   └── 数据源抽象
├── AiDataGateway.Infrastructure
│   ├── EF Core SQLite
│   ├── DPAPI/Credential Manager
│   ├── FreeSql/ADO.NET Provider
│   └── 日志与审计
├── AiDataGateway.SqlGuard
│   ├── SQL 分类
│   ├── 方言适配
│   ├── 对象提取
│   └── 风险规则
└── AiDataGateway.Web
    └── Vue 3 + Element Plus
```

发布时可将项目组合为一个可执行程序和少量运行文件，不要求用户安装 IIS。

## 6. 多数据源与多数据库支持

### 6.1 数据源模型

每一个数据库连接都注册为独立数据源：

```json
{
  "id": "project-a-dev",
  "name": "项目 A 开发库",
  "dbType": "SqlServer",
  "host": "10.0.1.10",
  "port": 1433,
  "database": "ProjectA_Dev",
  "credentialRef": "dpapi://project-a-dev",
  "accessMode": "ReadWriteWithApproval",
  "maxRows": 1000,
  "commandTimeoutSeconds": 30,
  "enabled": true,
  "tags": ["project-a", "dev"]
}
```

推荐字段：

| 字段 | 说明 |
|---|---|
| `id` | AI 调用和内部路由使用的稳定标识 |
| `dbType` | SQL Server、MySQL、PostgreSQL、Oracle、SQLite 等 |
| `host` / `port` | 数据库实际 IP、域名和端口 |
| `database` | 默认数据库或服务名 |
| `credentialRef` | 加密凭证引用，不保存到 AI 可见配置中 |
| `accessMode` | 只读、写入需审批、开发库允许写入 |
| `maxRows` | 单次最大返回行数 |
| `timeout` | SQL 执行超时 |
| `tags` | 项目、环境、用途等标签 |

### 6.2 连接实例管理

- 按 `dataSourceId` 创建和缓存独立 `IFreeSql` 或连接工厂。
- 不使用一个全局 ORM 实例切换不同连接字符串。
- 每个数据源拥有独立连接池、超时和健康状态。
- 数据源修改或密码更新后，安全释放旧实例并创建新实例。
- 长时间不用的数据源可回收连接实例，减少本机资源占用。
- 查询执行前检查本机到目标 IP 和端口的可达性，并给出明确错误信息。

### 6.3 数据库适配器

定义统一接口，屏蔽不同数据库 Provider 差异：

```csharp
public interface IDatabaseAdapter
{
    DatabaseType DatabaseType { get; }
    Task<TestConnectionResult> TestAsync(DataSourceConfig config);
    Task<SchemaSnapshot> GetSchemaAsync(DataSourceConfig config);
    Task<QueryPlanResult> ExplainAsync(SqlRequest request);
    Task<QueryResult> QueryAsync(SqlRequest request);
    Task<ExecuteResult> ExecuteAsync(SqlRequest request);
}
```

首期可按实际使用顺序支持数据库，不需要一次完成所有 Provider。建议先支持最常用的两种，再通过适配器扩展。

“多个数据库”包括两层含义：

1. 同一种数据库类型、不同 IP 或不同实例。
2. SQL Server、MySQL、PostgreSQL 等不同数据库类型。

两者都由 `dataSourceId + dbType` 路由解决。

## 7. AI 可调用工具

AI 工具列表保持小而明确：

| 工具 | 用途 | 是否直接执行 |
|---|---|---|
| `list_data_sources` | 查看当前允许使用的数据源别名和能力 | 否 |
| `describe_schema` | 读取授权范围内的库表字段 | 只读 |
| `validate_sql` | 检查 SQL、对象和风险等级 | 否 |
| `execute_query` | 执行只读查询 | 是 |
| `submit_change` | 提交 INSERT、UPDATE、DELETE、DDL 请求 | 进入审批 |
| `get_change_status` | 查询本地审批和执行结果 | 否 |

以下接口不提供给 AI：

- 批准或拒绝操作。
- 修改数据源连接信息。
- 查看明文数据库密码。
- 修改全局安全策略。
- 关闭审计或绕过 SQL 检查。

## 8. SQL 行为管控

### 8.1 权限模式

每个数据源可独立选择：

| 模式 | 行为 |
|---|---|
| `ReadOnly` | 只允许查询，所有写操作拒绝 |
| `ReadWriteWithApproval` | 查询自动执行，写操作弹窗确认 |
| `Development` | 可配置部分写操作自动执行，危险操作仍确认 |
| `Disabled` | 禁止 AI 使用该数据源 |

### 8.2 风险分级

| 风险级别 | 示例 | 默认处理 |
|---|---|---|
| 低 | 有条件、有限行数的 SELECT | 自动执行 |
| 中 | 大结果集、复杂 JOIN、慢查询风险 | 提示或确认 |
| 高 | INSERT、UPDATE、DELETE | 本机人工审批 |
| 极高 | DROP、TRUNCATE、ALTER、无条件 UPDATE/DELETE | 二次确认或直接拒绝 |

### 8.3 防护原则

- 不只使用正则表达式判断 SQL，应先完成语句拆分、注释处理和词法/语法级分类。
- 不同数据库方言使用独立规则，无法可靠解析时默认不执行危险语句。
- 数据库账号权限是最终边界：只读数据源尽量使用数据库只读账号。
- 默认禁止一次请求执行多条 SQL。
- 默认限制返回行数、执行时间和结果大小。
- 可对手机号、身份证、邮箱、密钥等字段进行本地脱敏。

## 9. 本机审批交互

写操作不需要接入钉钉或企业微信，直接由 WinForms 本机完成审批。

### 9.1 交互流程

1. AI 调用 `submit_change`。
2. 系统完成 SQL 预检并生成影响摘要。
3. 托盘图标闪烁或弹出 Windows 通知。
4. 用户打开 WebView2 审批页面。
5. 页面展示数据源、SQL、参数、风险、预计影响行数和回滚建议。
6. 用户选择批准、拒绝或编辑后重新提交。
7. 批准后由后台执行器执行，并把结果返回给 AI。

### 9.2 防止 AI 自己批准

即使审批页面和 MCP API 运行在同一进程，也必须区分调用入口：

- AI 使用独立的本地服务令牌。
- 审批接口要求 WinForms 启动时创建的 UI 会话令牌。
- UI 会话令牌仅通过 WebView2 初始化过程注入，不返回给 MCP 工具。
- 审批内容绑定 SQL、参数、数据源和时间戳；修改后自动失效。

## 10. 本地管理界面

Vue 3 + Element Plus 页面建议包含：

### 10.1 首页

- 服务运行状态和本地监听地址。
- AI 客户端连接状态。
- 数据源在线数量。
- 待审批操作。
- 最近查询与拦截记录。
- 全局“仅允许只读”快捷开关。

### 10.2 数据源管理

- 新增、复制、编辑和禁用数据源。
- 选择数据库类型并填写 IP、端口和数据库名。
- 测试网络和数据库连接。
- 浏览数据库、Schema、表和字段。
- 设置访问模式、返回行数、超时和脱敏策略。

### 10.3 审批中心

- 查看 SQL 格式化结果和参数。
- 高亮危险关键字和无条件写操作。
- 展示数据库类型、IP、数据库名和目标表。
- 批准、拒绝、修改后重新验证。
- 查看历史执行结果。

### 10.4 审计记录

- 按数据源、AI 客户端、SQL 类型和时间查询。
- 查看 SQL 指纹、耗时、行数和处理结果。
- 对敏感参数和结果进行掩码。
- 支持导出个人排查所需的审计记录。

## 11. 登录、用户管理与 OAuth2 认证

### 11.1 认证组件

不建议自行编写用户名密码校验和 JWT 生成逻辑。推荐在同一个 ASP.NET Core/Kestrel 进程中集成：

- **ASP.NET Core Identity**：用户、密码哈希、角色、锁定、双因素认证和安全标记。
- **OpenIddict Server**：实现 OAuth2/OpenID Connect 授权服务器和标准协议端点。
- **OpenIddict Validation**：验证本机 API 收到的 Access Token。
- **EF Core SQLite**：持久化 Identity 用户、角色以及 OpenIddict 客户端、授权和 Token 数据。

认证服务器、业务 API 和管理页面首期运行在同一个本地 Kestrel 实例中，不需要额外部署身份服务器。

### 11.2 标准登录功能

管理页面提供完整的本地登录功能：

- 用户名或邮箱 + 密码登录。
- 修改密码、管理员重置密码。
- 登录失败计数和临时锁定。
- 用户启用、禁用和强制下线。
- 首次登录强制修改密码。
- 可选 TOTP 动态验证码双因素认证。
- 当前登录设备、活动 Token 和最近登录记录查看。
- 注销当前会话或吊销用户全部 Token。

首次启动由初始化向导创建第一个 `Administrator` 用户。系统不得内置固定默认密码；管理员密码只在初始化时设置。

### 11.3 用户与角色

建议内置以下角色：

| 角色 | 权限 |
|---|---|
| `Administrator` | 用户、角色、OAuth 客户端、数据源和全局策略管理 |
| `Operator` | 数据源维护、连接测试和普通运行管理 |
| `Approver` | 审批或拒绝 AI 提交的数据库变更 |
| `Auditor` | 查看审计、登录记录和 Token 使用记录，不可执行 SQL |
| `Developer` | 使用授权数据源查询并提交变更申请 |
| `Viewer` | 只读查看个人可访问的数据源和记录 |

角色负责粗粒度权限，具体数据源访问使用 Claim 或用户—数据源授权表控制。例如同一个 `Developer` 用户只能访问被分配的 `project-a-dev`，不能自动访问其他数据库。

### 11.4 OAuth2/OIDC 客户端

用户和 AI 客户端是两种不同主体：

- 用户通过交互式登录获得身份。
- AI/MCP 客户端以 OAuth Client 身份获取受限 Token。

后台提供 OAuth 客户端管理页面，支持：

- 创建、禁用和删除客户端。
- 生成并轮换 `client_secret`。
- 设置允许的 Grant Type、Scope、回调地址和 Token 有效期。
- 限定客户端能够访问的数据源和工具。
- 查看客户端最近取证、调用和失败记录。
- 立即吊销客户端所有有效 Token。

推荐的客户端类型：

| 客户端 | OAuth2 流程 | 用途 |
|---|---|---|
| Vue/WebView2 管理端 | Authorization Code + PKCE | 用户登录、管理和审批 |
| 本地 AI/MCP 客户端 | Client Credentials | 无人值守调用只读工具或提交变更 |
| 桌面交互客户端 | Authorization Code + PKCE | 需要绑定具体用户身份的 AI 调用 |
| 无法弹浏览器的客户端 | Device Authorization，可选 | 后续兼容其他终端 |

不建议使用 Resource Owner Password Grant，也不让 AI 保存用户的账号密码。

### 11.5 Scope 设计

建议定义：

| Scope | 权限 |
|---|---|
| `openid` | 获取标准 OIDC 用户标识 |
| `profile` | 获取用户名等基础信息 |
| `offline_access` | 在允许时签发 Refresh Token |
| `gateway.datasource.read` | 查看允许的数据源与 Schema |
| `gateway.query.execute` | 执行只读查询 |
| `gateway.change.submit` | 提交写操作审批，不代表可以批准 |
| `gateway.change.approve` | 批准或拒绝变更，仅授予用户端 |
| `gateway.audit.read` | 查看审计记录 |
| `gateway.admin` | 用户、客户端、数据源和策略管理 |

AI 客户端默认只分配前三类网关业务 Scope：数据源读取、查询执行和变更提交。`gateway.change.approve` 与 `gateway.admin` 不得授予 AI 客户端。

### 11.6 Token 签发与验证

Token 由本地 OpenIddict 服务器签发，不是调用第三方云认证平台：

- Access Token 使用 RSA X.509 证书进行非对称签名。
- 签名证书与 HTTPS 证书分开管理。
- 私钥存放在 Windows 当前用户或本机证书存储中，不写入普通配置文件。
- 本地开发首次运行可生成并持久化自签名证书。
- API 使用本地 OpenIddict Validation 验证签名、签发者、受众、有效期、Scope 和 Token 状态。
- 支持签名证书轮换，新旧证书在过渡期并存。
- Access Token 建议短期有效；Refresh Token 和 Client Secret 均可单独吊销。
- 不建议使用所有客户端共享的 HS256 对称密钥。

Access Token 至少包含：

```text
sub          用户 ID 或客户端 ID
client_id    OAuth 客户端标识
name         用户显示名（用户 Token）
role         用户角色
scope        已授权 Scope
jti          Token 唯一标识
iss / aud    签发者和目标 API
exp / iat    过期与签发时间
```

数据源列表较大或经常变化时，不把完整数据源权限写死在 JWT 中。Token 保存主体和 Scope，API 执行请求时再从本地授权表读取最新的数据源权限，确保管理员撤权后立即生效。

### 11.7 标准协议端点

建议提供以下端点：

```text
/.well-known/openid-configuration
/connect/authorize
/connect/token
/connect/userinfo
/connect/logout
/connect/revocation
/connect/introspect        # 需要时启用
```

业务 API 使用 Bearer Token：

```http
Authorization: Bearer <access_token>
```

同一进程内的授权服务器和 API 可直接共享验证配置；如果未来拆分为 Windows Service、IIS 或独立服务，API 可通过标准 OIDC Discovery 获取公开签名信息。

### 11.8 认证与数据库审批的关系

认证成功不等于可以执行数据库操作。每次请求依次校验：

1. Token 是否有效。
2. OAuth 客户端是否启用。
3. 用户是否启用且未锁定。
4. Token 是否具有所需 Scope。
5. 用户或客户端是否被授权访问目标数据源。
6. 数据源当前访问模式是否允许该 SQL 类型。
7. 写操作是否由具有 `Approver` 权限的用户批准。

审批记录必须保存审批用户 ID、客户端 ID、Token `jti`、SQL 快照和审批时间。AI 的 Client Credentials Token 即使拥有 `gateway.change.submit`，也不能获得 `gateway.change.approve`。

## 12. 本地安全与配置存储

### 12.1 文件位置

建议将本机数据放在用户目录下的独立应用文件夹，例如：

```text
%LocalAppData%/AiDataGateway/
├── gateway.db
├── logs/
├── exports/
└── settings.json
```

其中：

- `gateway.db` 保存数据源元信息、策略、审批和审计索引。
- 数据库密码使用 DPAPI CurrentUser 加密后保存，或保存在 Windows Credential Manager。
- `settings.json` 只保存监听端口、界面设置和凭证引用，不保存明文密码。

### 12.2 本地监听

- 默认只绑定 `127.0.0.1`，避免局域网其他设备调用。
- AI 客户端通过已注册的 OAuth Client 使用 Client Credentials 获取短期 Access Token。
- 如果后续需要其他机器访问，可显式切换到局域网监听，并启用 HTTPS、OAuth2 和访问来源限制。
- Token 签名私钥、客户端 Secret 和数据库凭证分别保护，不能使用同一密钥。
- 管理和审批接口使用不同权限，不因“都是本机”而共用万能令牌。

## 13. 发布与启动方式

首期推荐提供以下形式：

### 13.1 普通绿色版

```text
AiDataGateway/
├── AiDataGateway.exe
├── appsettings.json
├── wwwroot/
└── 数据库 Provider 依赖
```

解压后直接运行，不需要 IIS。可增加“开机启动”和“最小化到托盘”选项。

### 13.2 自包含发布

可以使用 .NET Windows 自包含发布，使目标机器无需单独安装 .NET Runtime。WebView2 使用系统已有的 Evergreen Runtime；若目标机器没有，可在安装包中检测并提示安装。

### 13.3 可选部署方式

- IIS：仅当后续希望把本地服务变成常驻站点时使用。
- Windows Service：可将 API/执行核心拆成服务，WinForms 只做管理端。
- Docker：仅用于特殊交付场景，不作为当前产品方向。

## 14. 实施计划

### 第一阶段：本地骨架与数据源管理

- WinForms、托盘和 WebView2 宿主。
- WinForms 内启动 ASP.NET Core Kestrel。
- Vue 3 + Element Plus 基础页面。
- SQLite 配置库和 DPAPI 凭证保存。
- ASP.NET Core Identity、首个管理员初始化和角色管理。
- OpenIddict OAuth2/OIDC 端点、签名证书和 Token 验证。
- 数据源新增、测试连接和启停。

### 第二阶段：只读查询闭环

- MCP/WebAPI 接入。
- 数据源列表和 Schema 浏览。
- SQL 分类和只读校验。
- 多数据源动态路由。
- 查询行数、超时、结果大小和日志限制。

### 第三阶段：本地审批与受控写入

- 写操作提交和状态机。
- 托盘提醒与审批页面。
- SQL、参数和数据源不可变快照。
- 批准后执行、拒绝、超时和失败处理。

### 第四阶段：多数据库适配与发布

- 按实际需求增加 SQL Server、MySQL、PostgreSQL 等适配器。
- 数据库方言测试和错误码统一。
- 绿色版或自包含发布。
- AI 客户端配置向导和本地运行说明。

## 15. 首期 MVP 范围

建议首期只做以下能力：

1. WinForms 托盘程序。
2. 内嵌 Vue 3 + Element Plus 管理页面。
3. 本机 Kestrel WebAPI/MCP 服务。
4. ASP.NET Core Identity 标准登录、用户和角色管理。
5. OpenIddict OAuth2/OIDC 授权服务器。
6. Authorization Code + PKCE 和 Client Credentials 流程。
7. 本地 RSA 证书签名 JWT、Token 验证和吊销。
8. SQLite 本地配置、Identity、OpenIddict 和审计数据。
9. 支持两种最常用数据库类型。
10. 支持任意数量、不同 IP 的数据源配置。
11. 只读查询自动执行，写操作本机弹窗审批。
12. DROP、TRUNCATE 默认拒绝。
13. 查询行数、超时、脱敏和审计日志。

暂不纳入首期：

- 多租户 SaaS 管理平台。
- IIS、Kubernetes 和强制容器化。
- 钉钉、企业微信等外部审批。
- 复杂企业 IAM 和集中密钥平台。
- 多节点高可用和分布式任务调度。

## 16. 验收标准

1. 一个 WinForms 程序能够同时启动桌面界面和本地 WebAPI。
2. WebView2 能正常加载 Vue 管理端，窗口关闭后可继续在托盘运行。
3. 首次启动能够创建管理员，且系统不存在固定默认密码。
4. 支持用户启停、角色授权、密码重置、锁定和 Token 强制吊销。
5. Vue/WebView2 能通过 Authorization Code + PKCE 登录。
6. AI 客户端能通过 Client Credentials 获取受限 Access Token。
7. API 会校验 JWT 签名、签发者、受众、有效期、Scope 和客户端状态。
8. AI Token 不包含审批或管理 Scope，无法为自己的请求放行。
9. 能配置多个不同 IP、端口和数据库类型的数据源。
10. AI 无法读取数据库明文密码和 Token 签名私钥。
11. AI 能完成数据源查看、Schema 查看和安全查询。
12. 只读数据源上的写操作始终被拒绝。
13. 需要审批的写操作未经授权用户确认不能执行。
14. 查询超时、行数超限、连接失败和 SQL 不兼容时返回明确错误。
15. 登录、取证、数据库请求、审批和执行均可在本地审计页面中追溯。

## 17. 最终建议

本项目最合适的形态不是 IIS 网关或容器平台，而是一个“带本地服务能力的 WinForms 桌面工具”：

- WinForms 负责启动、托盘、通知和生命周期。
- ASP.NET Core Kestrel 负责 MCP、WebAPI 和 Vue 静态资源。
- WebView2 提供统一管理和审批界面。
- ASP.NET Core Identity 提供标准用户、密码和角色体系。
- OpenIddict 提供自建 OAuth2/OIDC 服务以及标准 Token 签发、验证和吊销能力。
- EF Core SQLite 管理本地配置、Identity、OpenIddict 与审计数据。
- FreeSql 或 ADO.NET 负责动态访问不同 IP、不同类型的目标数据库。
- 数据源账号权限、SQL 检查和本机人工确认共同约束 AI 的数据库行为。

这样既保持部署小巧，也能在后续需要时平滑扩展为 Windows Service、IIS 或独立网关服务。
