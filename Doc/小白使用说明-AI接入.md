# AiDataGateway 小白使用说明：客户端操作与 AI 接入

## 1. 这个软件是做什么的

AiDataGateway 是 AI 和数据库之间的一道“安全门”。

正常情况下，AI 不直接拿数据库账号连接数据库，而是按下面的路线访问：

```text
你提出需求
    ↓
AI 生成 SQL
    ↓
AiDataGateway 检查 SQL
    ↓
只读查询：直接执行并返回结果
写入操作：生成审批单，等你批准后执行
```

这样可以避免 AI 因为理解错误直接修改或删除数据库数据。

## 2. 使用前要准备什么

开始前需要准备：

1. 已编译好的 AiDataGateway；
2. 目标数据库的地址、端口、数据库名、用户名和密码；
3. 一个可以调用 HTTP API 的本地 AI 客户端或适配器；
4. 首次初始化时生成的 AI `Client ID` 和 `Client Secret`。

当前支持：

- SQL Server；
- MySQL；
- PostgreSQL；
- SQLite。

## 3. 启动客户端

双击运行：

```text
AiDataGateway.Desktop.exe
```

开发目录中的默认位置是：

```text
D:\WorkStation\DataGateway\src\AiDataGateway.Desktop\bin\Debug\net10.0-windows10.0.17763.0\AiDataGateway.Desktop.exe
```

程序启动后会出现管理窗口，并在本机启动一个服务：

```text
http://127.0.0.1:5127
```

关闭窗口时程序通常只是缩小到系统托盘。要彻底停止服务，需要右键托盘图标并选择“退出”。

## 4. 第一次使用

第一次打开时，需要创建管理员账号。

填写：

- 用户名；
- 邮箱；
- 显示名称；
- 管理员密码。

密码至少 10 位，并且必须同时包含：

- 大写字母；
- 小写字母；
- 数字。

例如：

```text
GatewayAdmin2026
```

初始化成功后会显示：

```text
Client ID
Client Secret
```

请立即保存。`Client Secret` 关闭提示后不能再次查看。

## 5. 客户端页面怎么用

### 5.1 概览

显示当前的数据源数量、待审批数量、用户数量和 OAuth 客户端数量。

### 5.2 数据源

在这里登记 AI 可以访问的数据库。

点击“新增数据源”，填写：

| 字段 | 怎么填 |
|---|---|
| 标识 | 给数据库起一个不重复的英文标识，例如 `order-dev` |
| 名称 | 容易看懂的名称，例如“订单系统开发库” |
| 类型 | SQL Server、MySQL、PostgreSQL 或 SQLite |
| IP/主机 | 数据库服务器 IP 或主机名 |
| 端口 | SQL Server 常用 1433，MySQL 3306，PostgreSQL 5432 |
| 数据库 | 数据库名称；SQLite 填数据库文件完整路径 |
| 用户名 | 网关访问数据库的账号 |
| 密码 | 网关访问数据库的密码 |
| 访问模式 | 建议选择“只读”或“写入需审批” |
| 最大返回行 | 建议先设置为 500 或 1000 |
| 超时秒数 | 一般使用 30 秒 |

保存后点击“测试”。看到连接成功，才表示网关能够访问该数据库。

### 5.3 审批

AI 提交 `INSERT`、`UPDATE`、`DELETE` 或 `CREATE TABLE` 后，会在这里出现审批单。

审批前应检查：

1. 数据源是不是正确；
2. SQL 修改的是不是正确的表；
3. `UPDATE`、`DELETE` 有没有正确的 `WHERE` 条件；
4. 预计影响多少行；
5. 是否应该先备份。

确认无误后点击“批准”。点击“拒绝”则不会执行 SQL。

### 5.4 用户

管理员可以创建其他管理用户，并为用户分配角色。

普通本地使用时，保留一个管理员账号即可；多人使用时再按职责创建账号。

### 5.5 OAuth 客户端

这里创建的是给 AI 使用的身份凭据，不是数据库账号。

新建后会显示一次 `Client Secret`。如果 Secret 丢失，请创建新的 OAuth 客户端并更新 AI 配置。

## 6. AI 到底需要哪些信息

需要把信息分成两类：

1. 配置到 AI 工具或适配器里的连接信息；
2. 可以在聊天中告诉 AI 的业务信息。

不要把这两类信息混在一起。

### 6.1 配置到 AI 工具中的信息

以下信息应配置到可信的本地 AI 工具、环境变量或适配器中：

| 配置项 | 默认值或来源 |
|---|---|
| 网关地址 | `http://127.0.0.1:5127` |
| Token 地址 | `http://127.0.0.1:5127/connect/token` |
| Client ID | 初始化或“OAuth 客户端”页面生成 |
| Client Secret | 初始化或“OAuth 客户端”页面生成，只显示一次 |
| Scope | `gateway.datasource.read gateway.query.execute gateway.change.submit` |
| API 文档 | `Doc/AI客户端API文档.md` |

推荐使用环境变量：

```text
AI_GATEWAY_BASE_URL=http://127.0.0.1:5127
AI_GATEWAY_TOKEN_URL=http://127.0.0.1:5127/connect/token
AI_GATEWAY_CLIENT_ID=<你的 Client ID>
AI_GATEWAY_CLIENT_SECRET=<你的 Client Secret>
AI_GATEWAY_SCOPE=gateway.datasource.read gateway.query.execute gateway.change.submit
```

不要把真实 Secret 提交到 Git，也不要把包含 Secret 的 `.env` 文件发给别人。

### 6.2 可以在聊天中告诉 AI 的信息

在对话中只需要告诉 AI：

- 想访问哪个数据源，例如“订单系统开发库”；
- 想查询或修改什么业务数据；
- 查询条件，例如订单号、时间范围、用户 ID；
- 需要返回哪些字段；
- 是否允许提交写操作审批；
- 写操作的预期结果。

例如：

```text
请通过 AiDataGateway 查询“订单系统开发库”。
查找最近 7 天状态为 Pending 的订单，只返回订单号、客户名称、金额和创建时间，最多返回 100 行。
先校验 SQL，再执行查询。
```

写操作示例：

```text
请通过 AiDataGateway 在“订单系统开发库”中，把订单号 ORD-10001 的状态改为 Cancelled。
先查询并展示当前记录，再生成带 WHERE 条件的 UPDATE，校验通过后只提交审批，不要声称已经执行。
```

建表示例：

```text
请通过 AiDataGateway 在“测试数据库”中创建 ApiTest 表。
先生成单条 CREATE TABLE SQL 并校验，然后提交人工审批。不要调用审批接口。
```

### 6.3 不需要告诉 AI 的信息

AI 不需要知道：

- 数据库真实密码；
- 数据库管理员账号；
- AiDataGateway 管理员密码；
- Windows 登录密码；
- Data Protection 密钥；
- 系统内部 `gateway.db` 的敏感字段。

目标数据库的 IP、账号和密码由 AiDataGateway 保存。AI 只需要通过数据源列表找到对应的数据源 ID。

## 7. Client Secret 应该放在哪里

可以把 Client Secret 理解成 AI 的“门禁钥匙”。

推荐顺序：

1. Windows 凭据管理器；
2. 仅当前用户可读的环境变量；
3. 可信本地适配器的安全配置；
4. 开发期间临时使用、且被 `.gitignore` 排除的 `.env` 文件。

不推荐：

- 直接粘贴到聊天窗口；
- 写进提示词；
- 写进源码；
- 提交到 Git；
- 放进公开文档或截图。

如果 Secret 曾经出现在聊天、截图或代码仓库中，应把它当成已经泄露，并换用新客户端。

## 8. AI 使用网关时的标准流程

### 8.1 查询数据

```text
AI 获取 Token
    ↓
AI 获取数据源列表
    ↓
AI 生成一条 SELECT
    ↓
AI 校验 SQL
    ↓
网关执行查询
    ↓
AI 向你展示结果
```

如果返回 `truncated=true`，表示结果超过最大返回行数。AI 应明确告诉你“结果已被截断”。

### 8.2 修改数据

```text
AI 先查询目标记录
    ↓
AI 生成带 WHERE 的写 SQL
    ↓
AI 校验 SQL
    ↓
AI 提交审批单
    ↓
你在客户端检查并批准
    ↓
网关执行 SQL
```

AI 收到 `202 Accepted` 或 `Pending` 只代表“审批单创建成功”，不代表数据库已经修改。

### 8.3 创建表

当前允许单条 `CREATE TABLE` 作为高风险操作提交审批。

以下操作仍然默认禁止：

- `DROP`；
- `ALTER`；
- `TRUNCATE`；
- `CREATE DATABASE`；
- `CREATE USER`；
- `GRANT`、`REVOKE`；
- 执行存储过程。

## 9. 可直接复制给 AI 的固定说明

下面这段文字不包含 Secret，可以直接放进 AI 的项目说明或提示词：

```text
本项目通过本机 AiDataGateway 访问数据库，网关地址由工具配置提供。

数据库操作规则：
1. 任何操作前先获取数据源列表，只使用网关返回的数据源 ID。
2. 生成 SQL 后必须先调用 SQL 校验接口。
3. 只读 SQL 才能调用查询接口。
4. INSERT、UPDATE、DELETE、CREATE TABLE 只能提交人工审批。
5. 收到 HTTP 202 或 Pending 后必须停止，并提示我到 AiDataGateway 客户端审批；不得声称写操作已执行。
6. 不得调用 /api/admin、/api/approvals、/api/setup 或用户管理接口。
7. 禁止多条 SQL、无 WHERE 的 UPDATE/DELETE、DROP、ALTER、TRUNCATE、授权语句和存储过程。
8. 默认只查询完成任务需要的最少字段和最少数据，不使用 SELECT *。
9. truncated=true 时必须说明查询结果被截断。
10. 不得索取、输出或保存数据库密码、管理员密码和 Client Secret。
```

## 10. 如果 AI 只支持 MCP

当前 AiDataGateway 提供的是 HTTP API，还没有内置 MCP Server。

如果你的 AI 客户端只支持 MCP，不能直接填写这个 HTTP 地址，需要增加一个 MCP-to-HTTP 适配层，将下面四个工具转发到网关：

| AI 工具 | HTTP 接口 |
|---|---|
| 获取数据源 | `GET /api/gateway/datasources` |
| 校验 SQL | `POST /api/gateway/sql/validate` |
| 执行只读查询 | `POST /api/gateway/query` |
| 提交写操作审批 | `POST /api/gateway/changes` |

工具的 JSON Schema 和完整请求示例见 [AI 客户端 API 文档](./AI客户端API文档.md)。

## 11. 第一次接入建议这样测试

按下面顺序测试，不要一开始就操作正式业务表：

1. 启动 AiDataGateway；
2. 在浏览器或终端访问 `/api/health`，确认返回 `status=ok`；
3. 让 AI 获取数据源列表；
4. 让 AI 执行 `SELECT 1 AS value`；
5. 在测试数据库创建专用测试表；
6. 分别测试 INSERT、SELECT、UPDATE 和 DELETE；
7. 确认每个写操作都必须在客户端人工批准；
8. 确认无 `WHERE` 的 DELETE 会被拦截；
9. 测试完成后再接入真实业务库。

生产数据库建议先设置为“只读”。确认流程稳定后，再根据需要改为“写入需审批”。

## 12. 常见问题

### 12.1 AI 提示连接失败

检查：

- AiDataGateway 是否正在运行；
- 系统托盘是否有程序图标；
- `http://127.0.0.1:5127/api/health` 是否能访问；
- AI 是否运行在同一台电脑；
- Base URL 和端口是否正确。

### 12.2 AI 获取 Token 失败

检查 Client ID 和 Client Secret。Secret 中可能包含 `+`、`/`、`=`，Token 请求必须使用标准表单编码，不能直接拼接字符串。

### 12.3 查询返回 401

表示 Token 缺失、错误或已过期。让适配器重新申请 Token。

### 12.4 查询返回 403

表示 AI 客户端没有需要的 Scope。重新创建或检查 OAuth 客户端配置。

### 12.5 写操作没有执行

查看客户端“审批”页面。AI 提交后必须由人点击批准。

### 12.6 审批单消失或无法批准

审批单默认有效期为 15 分钟。超时后重新让 AI 提交。

### 12.7 AI 一直查询审批状态

当前版本还没有提供给 AI 的审批状态查询接口。AI 提交后应停止，并等待你在客户端处理。

## 13. 记住这三句话

1. 数据库账号和密码只填写在 AiDataGateway，不要发给 AI。
2. Client Secret 配置到可信工具里，不要粘贴到聊天窗口。
3. AI 只能提写操作，最终是否执行由你在审批页面决定。
