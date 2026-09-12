# 本地日志查看器（WPF / .NET Framework 4.8）

这是从 AI 数据网关中拆出的独立桌面工具，面向人工运维和研发人员使用。它不启动 Web API，不提供 MCP 工具，也不需要运行原网关后端。

## 运行要求

- Windows 10/11
- .NET Framework 4.8 Runtime（Windows 11 通常已包含 4.8 或更高兼容版本）

运行 `AiDataGateway.LocalLogViewer.exe` 即可。程序默认打开“应用日志”页面；日志源和数据库连接分别在左侧的独立配置页面维护，日志详情也会进入独立子页面。所有设置自动保存在程序目录旁的 `Config` 文件夹：

- `Config/log-profiles.json`：多个本地日志配置；
- `Config/database-profiles.json`：多个数据库配置；
- `.bak`：上一次保存的备份。

建议将程序放在当前用户有写权限的目录中。程序会尽量把 `Config` 目录权限限制为当前 Windows 用户和 SYSTEM。

## 日志配置

1. 在左侧“日志配置”中单击“新增”。
2. 填写配置名称、日志根目录；选择目录时可选中目录中的任意日志文件。
3. 可指定 `NLog.config`；留空时会在日志目录及子目录中自动发现。
4. 文件模式默认为 `*.log`，会递归读取日期子目录。
5. 单文件读取上限为 1–10 MB；超过上限的文件直接跳过，避免一次占用过多内存。
6. 保存后选择日期、级别、关键词并单击“查询”。

解析支持 NLog Layout、JSON 行日志、消息字段中的嵌套 JSON、多行异常、空字段以及文件末尾没有换行符的记录。查询结果每页 100 条，最多保留 10000 条。

多个日志源中只能有一个“当前使用”项，应用日志页始终查询该日志源。日志中识别到 `sql`、`query`、`commandText` 等字段或 SQL 正文后，可进入独立的“SQL 分析”页面进行格式化。日志源可关联一个已保存的数据库连接；仅单条 `SELECT`/`WITH` 能进行只读试查，最多返回 200 行，写入、多语句和 `SELECT INTO` 会被阻止。

## 数据库配置与安全

数据库页可保存 SQL Server、MySQL、PostgreSQL、Oracle、SQLite 或自定义 ADO.NET Provider 的连接参数。除系统自带的 SQL Server Provider 外，测试其他数据库连接前，需要在程序目录部署并注册对应的 .NET Framework ADO.NET Provider。

密码不会写入连接字符串或明文配置。保存时使用 Windows DPAPI 的 `CurrentUser` 范围加密：

- 只有保存密码的 Windows 用户可解密；
- 把配置文件复制到其他用户或其他电脑不能直接解密；
- 编辑已有配置时密码留空会保留原密文；
- 更换 Windows 账号或重装系统前，应重新录入密码。

DPAPI 保护的是静态配置文件。拥有当前 Windows 账号且能控制当前进程的恶意软件仍可能读取进程内数据，因此日常仍应使用普通用户运行并保护 Windows 登录凭据。

## 编译

```powershell
dotnet build src/AiDataGateway.LocalLogViewer/AiDataGateway.LocalLogViewer.csproj -c Release
```

输出目录：`src/AiDataGateway.LocalLogViewer/bin/Release/net48`。

执行内置自检：

```powershell
AiDataGateway.LocalLogViewer.exe --self-test --self-test-output self-test.txt
```
