# AiDataGateway Monitor Agent 快速使用

本程序可同时上报服务器指标，并在远端按需读取本机 NLog 文件。它是 .NET 10 自包含程序，无需另装 .NET Runtime。

```powershell
AiDataGateway.MonitorAgent.exe `
  --gateway http://网关IP:5127 `
  --target 网关中创建的节点标识 `
  --secret "创建节点时显示的密钥" `
  --interval 10 `
  --log-path "D:\Logs\MyApp" `
  --nlog-config "D:\MyApp\NLog.config" `
  --nlog-target File `
  --listen http://0.0.0.0:5188
```

- 只采集指标时，省略 `--log-path`、`--nlog-config` 和 `--listen`。
- 日志目录、完整文件名或通配符均可作为 `--log-path`。
- 只在可信内网向网关电脑开放 5188 端口。
- 在网关新增“远程 Agent”日志源，地址填 `http://远程服务器IP:5188`，访问密钥填同一个 `--secret`。
- 完整说明见主项目 `Doc/服务器监控使用说明.md` 和 `Doc/项目与日志接入说明.md`。
