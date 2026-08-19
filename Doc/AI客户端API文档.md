# AiDataGateway AI 客户端 API 文档

## 1. 文档范围

本文档面向连接 AiDataGateway 的 AI 编程助手、Agent 或本地适配器。

当前版本提供本地 HTTP/JSON API：

```text
Base URL: http://127.0.0.1:5127
```

当前尚未提供 MCP Server 端点。如 AI 客户端只支持 MCP，需要在其外部增加一个 MCP-to-HTTP 适配层，将工具调用转换为本文档中的 HTTP 请求。

AI 客户端只应使用以下业务能力：

- 获取可用数据源；
- 校验 SQL；
- 执行只读查询；
- 提交写操作审批单。

审批、用户管理、OAuth 客户端管理和数据源管理接口不得暴露为 AI 工具。

## 2. 鉴权

### 2.1 获取 Access Token

使用初始化或管理员页面生成的 `client_id` 和 `client_secret` 调用：

```http
POST /connect/token HTTP/1.1
Host: 127.0.0.1:5127
Content-Type: application/x-www-form-urlencoded

grant_type=client_credentials&client_id=<CLIENT_ID>&client_secret=<CLIENT_SECRET>&scope=gateway.datasource.read%20gateway.query.execute%20gateway.change.submit
```

默认 Scope：

| Scope | 用途 |
|---|---|
| `gateway.datasource.read` | 读取对 AI 可见的数据源列表 |
| `gateway.query.execute` | 执行只读 SQL |
| `gateway.change.submit` | 提交写操作审批单 |

AI 默认不具备 `gateway.change.approve` 或 `gateway.admin`。

> Client Secret 是 Base64 字符串，可能包含 `+`、`/` 和 `=`。必须使用标准表单编码，不能直接拼接未编码的请求字符串，否则 `+` 可能被解析为空格。

使用 curl：

```bash
curl -X POST "http://127.0.0.1:5127/connect/token" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  --data-urlencode "grant_type=client_credentials" \
  --data-urlencode "client_id=<CLIENT_ID>" \
  --data-urlencode "client_secret=<CLIENT_SECRET>" \
  --data-urlencode "scope=gateway.datasource.read gateway.query.execute gateway.change.submit"
```

使用 PowerShell：

```powershell
$tokenResponse = Invoke-RestMethod `
  -Method Post `
  -Uri "http://127.0.0.1:5127/connect/token" `
  -ContentType "application/x-www-form-urlencoded" `
  -Body @{
    grant_type    = "client_credentials"
    client_id     = $env:AI_GATEWAY_CLIENT_ID
    client_secret = $env:AI_GATEWAY_CLIENT_SECRET
    scope         = "gateway.datasource.read gateway.query.execute gateway.change.submit"
  }

$accessToken = $tokenResponse.access_token
```

典型响应：

```json
{
  "access_token": "<ACCESS_TOKEN>",
  "token_type": "Bearer",
  "expires_in": 3600,
  "scope": "gateway.datasource.read gateway.query.execute gateway.change.submit"
}
```

`expires_in` 以服务端实际响应为准。客户端应缓存 Token，并在过期或收到 401 后重新申请。

### 2.2 携带 Token

除健康检查外，本文档中的业务请求均应携带：

```http
Authorization: Bearer <ACCESS_TOKEN>
```

不要把 Client Secret 发送给业务 API，也不要将 Secret 放入大模型上下文。

## 3. 接口总览

| 方法 | 路径 | 鉴权要求 | 用途 |
|---|---|---|---|
| `GET` | `/api/health` | 无 | 检查网关是否运行 |
| `GET` | `/api/gateway/datasources` | `gateway.datasource.read` | 获取可用数据源 |
| `POST` | `/api/gateway/sql/validate` | Bearer Token | 分析 SQL 风险 |
| `POST` | `/api/gateway/query` | `gateway.query.execute` | 执行只读 SQL |
| `POST` | `/api/gateway/changes` | `gateway.change.submit` | 提交写操作审批单 |

## 4. 健康检查

```http
GET /api/health
```

响应：

```json
{
  "status": "ok",
  "version": "1.0.0.0"
}
```

调用示例：

```bash
curl "http://127.0.0.1:5127/api/health"
```

## 5. 获取可用数据源

```http
GET /api/gateway/datasources
Authorization: Bearer <ACCESS_TOKEN>
```

响应示例：

```json
[
  {
    "id": "11111111-1111-1111-1111-111111111111",
    "key": "customer-a-dev",
    "name": "Customer A Development",
    "provider": 2,
    "accessMode": 3
  },
  {
    "id": "22222222-2222-2222-2222-222222222222",
    "key": "customer-a-prod",
    "name": "Customer A Production",
    "provider": 1,
    "accessMode": 1
  }
]
```

只返回已启用的数据源，不返回 IP、数据库账号或密码。

`provider`：

| 值 | 数据库 |
|---:|---|
| `1` | SQL Server |
| `2` | MySQL |
| `3` | PostgreSQL |
| `4` | SQLite |

`accessMode`：

| 值 | 模式 |
|---:|---|
| `0` | Disabled；正常情况下不会出现在此列表 |
| `1` | ReadOnly |
| `2` | ReadWriteWithApproval |
| `3` | Development；当前仍要求写操作审批 |

AI 必须使用返回的 `id` 作为后续请求的 `dataSourceId`，不得猜测或自行构造数据源 ID。

## 6. 校验 SQL

```http
POST /api/gateway/sql/validate
Authorization: Bearer <ACCESS_TOKEN>
Content-Type: application/json

{
  "sql": "SELECT id, name FROM users WHERE id = 1"
}
```

响应示例：

```json
{
  "allowed": true,
  "isReadOnly": true,
  "riskLevel": 1,
  "operation": "SELECT",
  "reasons": []
}
```

`riskLevel`：

| 值 | 风险 |
|---:|---|
| `1` | Low |
| `2` | Medium |
| `3` | High |
| `4` | Critical |

写操作校验示例：

```json
{
  "allowed": true,
  "isReadOnly": false,
  "riskLevel": 3,
  "operation": "UPDATE",
  "reasons": [
    "Write statements require local approval."
  ]
}
```

危险 SQL 示例：

```json
{
  "allowed": false,
  "isReadOnly": false,
  "riskLevel": 4,
  "operation": "DELETE",
  "reasons": [
    "Write statements require local approval.",
    "DELETE without a WHERE clause is blocked by default."
  ]
}
```

AI 应在调用查询或变更接口前先调用校验接口。如果 `allowed=false`，不得继续提交或尝试改用其他接口绕过。

## 7. 执行只读查询

```http
POST /api/gateway/query
Authorization: Bearer <ACCESS_TOKEN>
Content-Type: application/json

{
  "dataSourceId": "11111111-1111-1111-1111-111111111111",
  "sql": "SELECT id, name FROM users WHERE status = 'active'"
}
```

响应示例：

```json
{
  "columns": ["id", "name"],
  "rows": [
    {
      "id": 1,
      "name": "Alice"
    },
    {
      "id": 2,
      "name": "Bob"
    }
  ],
  "truncated": false
}
```

注意事项：

- 此接口只允许被策略识别为只读的单条 SQL；
- 返回行数受数据源 `maxRows` 限制；
- `truncated=true` 表示结果还有更多行，AI 必须向用户明确说明结果被截断；
- 建议 SQL 显式选择必要列并使用过滤条件；
- 不要依赖 `SELECT *`；当前策略会将其标记为中风险；
- 不要在一个请求中发送多条以分号分隔的 SQL；
- 数据库异常会返回错误，AI 不应通过反复改写 SQL 尝试绕过权限或安全策略。

## 8. 提交写操作审批单

仅对数据源模式 `2` 或 `3` 使用：

```http
POST /api/gateway/changes
Authorization: Bearer <ACCESS_TOKEN>
Content-Type: application/json

{
  "dataSourceId": "11111111-1111-1111-1111-111111111111",
  "sql": "UPDATE users SET status = 'disabled' WHERE id = 42"
}
```

成功响应：

```http
HTTP/1.1 202 Accepted
Location: /api/gateway/changes/33333333-3333-3333-3333-333333333333
```

```json
{
  "id": "33333333-3333-3333-3333-333333333333",
  "status": 1,
  "analysis": {
    "allowed": true,
    "isReadOnly": false,
    "riskLevel": 3,
    "operation": "UPDATE",
    "reasons": [
      "Write statements require local approval."
    ]
  }
}
```

`status`：

| 值 | 状态 |
|---:|---|
| `1` | Pending |
| `2` | Approved |
| `3` | Rejected |
| `4` | Executing |
| `5` | Succeeded |
| `6` | Failed |
| `7` | Expired |

收到 `202 Accepted` 后，AI 必须停止执行链并告诉用户：

- 写操作尚未执行；
- 审批单 ID；
- 需要在 AiDataGateway 桌面管理页面进行人工审核。

当前版本尚未实现 `GET /api/gateway/changes/{id}`。响应中的 `Location` 是为后续状态查询能力保留的地址，AI 当前不得轮询该地址，也不得把 404 解释为审批失败。

审批单有效期为 15 分钟。

## 9. SQL 安全策略

| SQL 类型 | 行为 |
|---|---|
| `SELECT`、`WITH`、`EXPLAIN`、`SHOW`、`DESCRIBE`、`PRAGMA` | 可进入只读查询接口 |
| `SELECT *` | 允许，但标记为 Medium 风险提示 |
| `INSERT`、`UPDATE`、`DELETE`、`MERGE`、`REPLACE` | 只能提交审批 |
| 无 `WHERE` 的 `UPDATE`/`DELETE` | Critical，拒绝提交 |
| `CREATE TABLE` | High，只能提交审批 |
| 其他 `CREATE`、`DROP`、`TRUNCATE`、`ALTER` | Critical，拒绝 |
| `GRANT`、`REVOKE`、`EXEC`、`EXECUTE` | Critical，拒绝 |
| 多条 SQL | Critical，拒绝 |
| 未识别操作 | Critical，拒绝 |

AI 不得使用注释、编码、存储过程、数据库特有语法或其他方式规避分类结果。

## 10. 错误处理

| HTTP 状态 | 含义 | AI 应采取的动作 |
|---:|---|---|
| `400` | 参数错误、SQL 被拦截、数据源模式不允许或数据库执行错误 | 读取响应 `message`/`analysis`；不要绕过策略 |
| `401` | Token 缺失、无效或过期 | 重新获取一次 Token；仍失败则停止并报告 |
| `403` | Token 缺少 Scope | 停止调用，通知管理员检查客户端权限 |
| `404` | 数据源不存在 | 重新获取数据源列表，不要猜测 ID |
| `202` | 写操作已进入审批 | 停止自动执行，提示用户人工审批 |
| `500` | 审批后数据库执行失败等服务端问题 | 停止重试并向用户报告错误摘要 |

常见错误响应：

```json
{
  "message": "The data source does not allow write requests."
}
```

带分析结果的错误：

```json
{
  "message": "The SQL is not an approvable write statement.",
  "analysis": {
    "allowed": false,
    "isReadOnly": false,
    "riskLevel": 4,
    "operation": "DROP",
    "reasons": ["DROP is disabled by the default policy."]
  }
}
```

客户端应设置合理超时，并避免对非幂等写操作自动重试。

## 11. 推荐的 AI 工具映射

如果 AI 平台需要声明工具，可在可信适配层中映射以下四个工具。适配层负责保存 Secret、获取 Token 和发送 HTTP 请求，大模型只能看到工具参数和业务响应。

### 11.1 `gateway_list_datasources`

```json
{
  "name": "gateway_list_datasources",
  "description": "列出当前 AI 有权访问且已启用的数据源。执行任何查询前调用。",
  "inputSchema": {
    "type": "object",
    "properties": {},
    "additionalProperties": false
  }
}
```

映射：`GET /api/gateway/datasources`

### 11.2 `gateway_validate_sql`

```json
{
  "name": "gateway_validate_sql",
  "description": "在执行或提交 SQL 前分析操作类型和安全风险。",
  "inputSchema": {
    "type": "object",
    "properties": {
      "sql": {
        "type": "string",
        "description": "单条 SQL，不允许包含第二条语句。"
      }
    },
    "required": ["sql"],
    "additionalProperties": false
  }
}
```

映射：`POST /api/gateway/sql/validate`

### 11.3 `gateway_execute_query`

```json
{
  "name": "gateway_execute_query",
  "description": "在指定数据源执行已通过安全校验的只读 SQL。",
  "inputSchema": {
    "type": "object",
    "properties": {
      "dataSourceId": {
        "type": "string",
        "format": "uuid"
      },
      "sql": {
        "type": "string"
      }
    },
    "required": ["dataSourceId", "sql"],
    "additionalProperties": false
  }
}
```

映射：`POST /api/gateway/query`

### 11.4 `gateway_submit_change`

```json
{
  "name": "gateway_submit_change",
  "description": "提交单条写 SQL 供人工审批。返回 202 只表示已提单，不表示已执行。",
  "inputSchema": {
    "type": "object",
    "properties": {
      "dataSourceId": {
        "type": "string",
        "format": "uuid"
      },
      "sql": {
        "type": "string"
      }
    },
    "required": ["dataSourceId", "sql"],
    "additionalProperties": false
  }
}
```

映射：`POST /api/gateway/changes`

不要向 AI 注册以下接口：

```text
/api/setup/*
/api/auth/*
/api/admin/*
/api/approvals/*
/api/audit/*
/api/events
/connect/authorize
/connect/logout
```

## 12. 可直接提供给 AI 的操作规则

可以把以下内容作为 AI 工具说明的一部分：

```text
你通过 AiDataGateway 访问数据库。

1. 执行任何数据库操作前，先调用 gateway_list_datasources，并只使用返回的数据源 ID。
2. 生成 SQL 后先调用 gateway_validate_sql。
3. 只读 SQL 仅通过 gateway_execute_query 执行。
4. 写 SQL 仅通过 gateway_submit_change 提交人工审批；收到 Pending/202 后必须停止，不得声称操作已经执行。
5. 禁止多语句、无 WHERE 的 UPDATE/DELETE、DDL、授权语句、存储过程执行以及任何绕过安全分类的尝试。
6. 不得调用管理员接口或审批接口，不得索取、输出或保存 Client Secret。
7. 查询结果 truncated=true 时必须告诉用户结果已被截断。
8. 401 时最多重新获取一次 Token；403、策略拒绝或数据库错误时停止并向用户说明。
9. 默认只读取完成任务所需的最少列和最少数据，不使用 SELECT *，避免查询敏感字段。
```

## 13. 最小调用顺序

```text
获取 Token
    ↓
GET /api/gateway/datasources
    ↓
POST /api/gateway/sql/validate
    ↓
isReadOnly = true                 isReadOnly = false
    ↓                                  ↓
POST /api/gateway/query           POST /api/gateway/changes
    ↓                                  ↓
返回查询结果                       返回 Pending，等待人工审批
```

## 14. 管理端实时接口（不得注册为 AI 工具）

以下接口仅供已登录的本地管理页面使用，使用登录 Cookie 和角色授权：

| 方法 | 地址 | 用途 |
|---|---|---|
| `GET` | `/api/approvals?take=500` | 查询审批历史 |
| `GET` | `/api/approvals/{id}` | 查询完整 SQL、审批及执行详情 |
| `POST` | `/api/approvals/{id}/review` | 人工批准或拒绝 |
| `GET` | `/api/audit/logs?take=500` | 查询调用与运行日志 |
| `GET` | `/api/events` | 建立 SSE 长连接，接收表变更通知 |

`/api/events` 由服务端在业务数据提交后主动发送 `gateway-change` 事件。管理页面收到事件后按动作类型刷新审批、日志、数据源、用户或 OAuth2 客户端列表，不使用定时轮询。
