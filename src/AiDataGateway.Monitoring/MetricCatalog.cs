namespace AiDataGateway.Monitoring;

public sealed record MetricDefinition(
    string Key,
    string Name,
    string Category,
    string Unit,
    string Description,
    bool Required = false,
    bool DefaultEnabled = false);

public static class MetricCatalog
{
    public static IReadOnlyList<MetricDefinition> All { get; } =
    [
        new("cpu.percent", "CPU 使用率", "处理器", "percent", "整台服务器的 CPU 使用率。", true, true),
        new("memory.percent", "内存使用率", "内存", "percent", "物理内存使用比例。", true, true),
        new("memory.used_bytes", "已用内存", "内存", "bytes", "已使用的物理内存。", false, true),
        new("memory.available_bytes", "可用内存", "内存", "bytes", "当前可供系统使用的物理内存。"),
        new("pagefile.percent", "交换区使用率", "内存", "percent", "Windows 页面文件或 Linux Swap 的使用比例。"),
        new("pagefile.used_bytes", "交换区已用", "内存", "bytes", "Windows 页面文件或 Linux Swap 的已用容量。"),
        new("disk.percent", "磁盘使用率", "存储", "percent", "所有已就绪本地磁盘的合计使用比例。", true, true),
        new("disk.used_bytes", "磁盘已用", "存储", "bytes", "所有已就绪本地磁盘的合计已用容量。", false, true),
        new("disk.free_bytes", "磁盘可用", "存储", "bytes", "所有已就绪本地磁盘的合计可用容量。"),
        new("network.received_total_bytes", "网络累计接收", "网络", "bytes", "启用网卡自系统启动以来累计接收的字节数。", false, true),
        new("network.sent_total_bytes", "网络累计发送", "网络", "bytes", "启用网卡自系统启动以来累计发送的字节数。", false, true),
        new("network.receive_bytes_per_second", "网络接收速率", "网络", "bytes_per_second", "相邻采样之间计算的网络接收速率。"),
        new("network.send_bytes_per_second", "网络发送速率", "网络", "bytes_per_second", "相邻采样之间计算的网络发送速率。"),
        new("process.cpu_percent", "Agent 进程 CPU", "采集进程", "percent", "网关或远端 Agent 进程自身的 CPU 使用率。"),
        new("process.working_set_bytes", "Agent 工作集", "采集进程", "bytes", "网关或远端 Agent 进程当前工作集。", false, true),
        new("process.private_memory_bytes", "Agent 专用内存", "采集进程", "bytes", "网关或远端 Agent 进程占用的专用内存。"),
        new("process.thread_count", "Agent 线程数", "采集进程", "count", "网关或远端 Agent 进程的线程数量。"),
        new("process.handle_count", "Agent 句柄数", "采集进程", "count", "网关或远端 Agent 进程的系统句柄数量。"),
        new("process.uptime_seconds", "Agent 运行时间", "采集进程", "duration_seconds", "网关或远端 Agent 进程本次启动后的运行时间。"),
        new("system.uptime_seconds", "系统运行时间", "系统", "duration_seconds", "服务器本次启动后的运行时间。", true, true),
        new("system.process_count", "系统进程数", "系统", "count", "当前服务器正在运行的进程数量。"),
        new("system.logical_processor_count", "逻辑处理器数", "系统", "count", "操作系统可用的逻辑处理器数量。"),
        new("system.tcp_connection_count", "TCP 连接数", "系统", "count", "当前活动 TCP 连接数量。"),
        new("gc.managed_memory_bytes", ".NET 托管内存", "运行时", "bytes", "网关或 Agent 当前分配的托管内存。"),
        new("gc.heap_size_bytes", ".NET GC 堆大小", "运行时", "bytes", "最近一次 GC 信息中的托管堆大小。")
    ];

    public static IReadOnlySet<string> KnownKeys { get; } = All.Select(item => item.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
    public static string[] RequiredKeys { get; } = All.Where(item => item.Required).Select(item => item.Key).ToArray();
    public static string[] DefaultKeys { get; } = All.Where(item => item.Required || item.DefaultEnabled).Select(item => item.Key).ToArray();

    public static string[] NormalizeSelection(IEnumerable<string>? values)
    {
        var selected = (values ?? DefaultKeys)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToLowerInvariant())
            .Where(KnownKeys.Contains)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        selected.UnionWith(RequiredKeys);
        return All.Where(item => selected.Contains(item.Key)).Select(item => item.Key).ToArray();
    }
}
