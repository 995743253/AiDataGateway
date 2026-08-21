# AiDataGateway MCP Server 接入说明

## 1. 地址与传输

AiDataGateway 内置无状态 MCP Streamable HTTP Server：

```text
http://127.0.0.1:5127/mcp
```

服务器兼容 MCP `2025-06-18`、`2025-03-26` 和 `2024-11-05`，初始化时优先协商客户端请求的受支持版本。当前没有需要服务端主动通知的能力，因此只使用 HTTP `POST /mcp`；`GET` 和 `DELETE` 返回 `405`，不提供旧版 `/sse` 端点。

## 2. OAuth2 鉴权

MCP 只接受 OAuth2 Bearer Token，不接受管理页面 Cookie。先使用初始化时保存或后台新建的 Client ID、Client Secret 获取 Token：

```http
POST http://127.0.0.1:5127/connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=client_credentials&
client_id=<CLIENT_ID>&
client_secret=<CLIENT_SECRET>&
scope=gateway.datasource.read gateway.query.execute gateway.change.submit
```

之后每个 MCP 请求携带：

```http
Authorization: Bearer <ACCESS_TOKEN>
Content-Type: application/json
Accept: application/json, text/event-stream
```

支持 OAuth Client Credentials 扩展的 MCP 客户端可以配置：

- MCP URL：`http://127.0.0.1:5127/mcp`
- Token URL：`http://127.0.0.1:5127/connect/token`
- Client ID、Client Secret：后台生成的凭据
- Scope：`gateway.datasource.read gateway.query.execute gateway.change.submit`

不支持自动换 Token 的客户端，应由可信的本地启动器或凭据组件获取和刷新 Token。不要把 Client Secret 写入 Prompt 或发给大模型。

授权发现地址：

```text
http://127.0.0.1:5127/.well-known/oauth-authorization-server
http://127.0.0.1:5127/.well-known/oauth-protected-resource
```

## 3. MCP 工具

| 工具 | Scope | 行为 |
|---|---|---|
| `list_data_sources` | `gateway.datasource.read` | 返回已启用数据源及其 ID、类型、访问模式、行数上限和表黑名单 |
| `validate_sql` | 已认证 | 只分析 SQL，不连接数据库 |
| `query_database` | `gateway.query.execute` | 执行只读 SQL，强制应用 SQL 策略、表黑名单和最大返回行数 |
| `submit_change` | `gateway.change.submit` | 仅生成待人工审批工单，不批准、不直接执行 SQL |
| `get_change_status` | `gateway.change.submit` | 查询指定工单的 Pending、Rejected、Succeeded、Failed 或 Expired 状态 |

MCP 没有审批工具。`submit_change` 返回 Pending 后，必须由 Administrator 或 Approver 在桌面管理页面审核。

## 4. 初始化示例

```http
POST /mcp HTTP/1.1
Host: 127.0.0.1:5127
Authorization: Bearer <ACCESS_TOKEN>
Content-Type: application/json
Accept: application/json, text/event-stream
MCP-Protocol-Version: 2025-06-18
Mcp-Method: initialize

{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "initialize",
  "params": {
    "protocolVersion": "2025-06-18",
    "capabilities": {},
    "clientInfo": {
      "name": "local-ai-client",
      "version": "1.0.0"
    }
  }
}
```

初始化完成后客户端可发送 `notifications/initialized`。通知没有 `id`，服务器返回 HTTP `202`。

## 5. 获取工具列表

```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/list",
  "params": {}
}
```

## 6. 只读查询

先调用 `list_data_sources` 获取真实 `dataSourceId`，再调用：

```json
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "tools/call",
  "params": {
    "name": "query_database",
    "arguments": {
      "dataSourceId": "11111111-1111-1111-1111-111111111111",
      "sql": "SELECT id, name FROM users WHERE id = 1"
    }
  }
}
```

成功结果同时提供文本 `content` 和机器可读的 `structuredContent`。如果 SQL 不是只读、引用黑名单表或违反安全策略，工具结果的 `isError` 为 `true`，且不会连接目标数据库。

## 7. 提交写操作审批

```json
{
  "jsonrpc": "2.0",
  "id": 4,
  "method": "tools/call",
  "params": {
    "name": "submit_change",
    "arguments": {
      "dataSourceId": "11111111-1111-1111-1111-111111111111",
      "sql": "UPDATE users SET display_name = 'Alice' WHERE id = 1"
    }
  }
}
```

返回的 `structuredContent` 包含 `id`、`status`、`analysis` 和 `expiresAtUtc`。`status=Pending` 只表示工单已创建，不代表 SQL 已执行。

审批有效期由管理员在“系统设置”中配置，默认 15 分钟，范围 1–10080 分钟。设置只影响新建工单。

## 8. 查询工单状态

```json
{
  "jsonrpc": "2.0",
  "id": 5,
  "method": "tools/call",
  "params": {
    "name": "get_change_status",
    "arguments": {
      "changeId": "22222222-2222-2222-2222-222222222222"
    }
  }
}
```

状态含义：

| 状态 | 含义 |
|---|---|
| `Pending` | 等待人工审批 |
| `Rejected` | 人工拒绝，未执行 |
| `Succeeded` | 已批准且执行成功 |
| `Failed` | 已批准但数据库执行失败 |
| `Expired` | 超过工单有效期，不能再批准 |

状态查询应由用户动作或合理退避机制触发，不要高频轮询。

## 9. 安全边界

1. MCP 只能使用 Bearer Token；管理页面 Cookie 不能调用。
2. MCP 不暴露审批、用户、OAuth 客户端、设置和数据源管理接口。
3. AI 只能提交写操作建议，批准和执行权仍由本地用户掌握。
4. REST 与 MCP 共用同一 SQL 检查、数据源权限、表黑名单、行数限制和审计日志。
5. `Mcp-Method`、`Mcp-Name` 请求头如存在，必须与 JSON-RPC 请求体一致，避免代理层与应用层理解不同。
