using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace AiDataGateway.Monitoring;

public sealed class SystemMetricsCollector
{
    private readonly object _sync = new();
    private CpuTimes? _previousCpuTimes;
    private NetworkSample? _previousNetwork;
    private ProcessCpuSample? _previousProcessCpu;

    public SystemMetricSnapshot Collect(IEnumerable<string>? enabledMetricKeys = null)
    {
        var selected = enabledMetricKeys?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool Wants(string key) => selected is null || selected.Contains(key);
        var collectedAt = DateTimeOffset.UtcNow;
        var memory = ReadMemory();
        var disk = ReadDisk();
        var network = ReadNetwork();
        using var process = Process.GetCurrentProcess();
        var extended = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        Add("memory.available_bytes", memory.Available);
        Add("pagefile.used_bytes", memory.PageFileUsed);
        Add("pagefile.percent", Percent(memory.PageFileUsed, memory.PageFileTotal));
        Add("disk.free_bytes", disk.Free);

        var networkRates = ReadNetworkRates(network, collectedAt);
        Add("network.receive_bytes_per_second", networkRates.ReceivedPerSecond);
        Add("network.send_bytes_per_second", networkRates.SentPerSecond);

        Add("process.cpu_percent", ReadProcessCpuPercent(process, collectedAt));
        Add("process.private_memory_bytes", SafeRead(() => process.PrivateMemorySize64));
        Add("process.thread_count", SafeRead(() => process.Threads.Count));
        Add("process.handle_count", SafeRead(() => process.HandleCount));
        Add("process.uptime_seconds", SafeRead(() => Math.Max(0, (collectedAt - process.StartTime.ToUniversalTime()).TotalSeconds)));
        Add("system.logical_processor_count", Environment.ProcessorCount);
        if (Wants("system.process_count")) extended["system.process_count"] = ReadProcessCount();
        if (Wants("system.tcp_connection_count")) extended["system.tcp_connection_count"] = ReadTcpConnectionCount();
        Add("gc.managed_memory_bytes", GC.GetTotalMemory(false));
        Add("gc.heap_size_bytes", GC.GetGCMemoryInfo().HeapSizeBytes);

        return new SystemMetricSnapshot(
            collectedAt,
            Environment.MachineName,
            RuntimeInformation.OSDescription,
            ReadCpuPercent(),
            memory.Used,
            memory.Total,
            disk.Used,
            disk.Total,
            network.Received,
            network.Sent,
            process.WorkingSet64,
            Math.Max(0, Environment.TickCount64 / 1_000),
            extended);

        void Add(string key, double value)
        {
            if (Wants(key) && double.IsFinite(value) && value >= 0) extended[key] = Math.Round(value, 2);
        }
    }

    private double ReadCpuPercent()
    {
        var current = ReadCpuTimes();
        if (current is null) return 0;

        lock (_sync)
        {
            var previous = _previousCpuTimes;
            _previousCpuTimes = current;
            if (previous is null) return 0;

            var totalDelta = current.Value.Total - previous.Value.Total;
            var idleDelta = current.Value.Idle - previous.Value.Idle;
            if (totalDelta <= 0) return 0;
            return Math.Round(Math.Clamp((totalDelta - idleDelta) * 100d / totalDelta, 0, 100), 2);
        }
    }

    private static CpuTimes? ReadCpuTimes()
    {
        if (OperatingSystem.IsWindows())
        {
            if (!GetSystemTimes(out var idle, out var kernel, out var user)) return null;
            var idleTicks = ToUInt64(idle);
            return new CpuTimes(idleTicks, ToUInt64(kernel) + ToUInt64(user));
        }

        if (OperatingSystem.IsLinux())
        {
            try
            {
                var values = File.ReadLines("/proc/stat").First().Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Skip(1).Select(value => ulong.Parse(value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                if (values.Length < 4) return null;
                var idle = values[3] + (values.Length > 4 ? values[4] : 0);
                return new CpuTimes(idle, values.Aggregate(0UL, (sum, value) => sum + value));
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private static MemoryData ReadMemory()
    {
        if (OperatingSystem.IsWindows())
        {
            var status = new MemoryStatusEx();
            if (GlobalMemoryStatusEx(status))
            {
                var total = SafeLong(status.TotalPhysical);
                var available = SafeLong(status.AvailablePhysical);
                var pageFileTotal = SafeLong(status.TotalPageFile);
                var pageFileAvailable = SafeLong(status.AvailablePageFile);
                return new MemoryData(Math.Max(0, total - available), total, available,
                    Math.Max(0, pageFileTotal - pageFileAvailable), pageFileTotal);
            }
        }

        if (OperatingSystem.IsLinux())
        {
            try
            {
                var values = File.ReadLines("/proc/meminfo")
                    .Select(line => line.Split(':', 2))
                    .Where(parts => parts.Length == 2)
                    .ToDictionary(parts => parts[0], parts => ParseKilobytes(parts[1]), StringComparer.OrdinalIgnoreCase);
                var total = values.GetValueOrDefault("MemTotal");
                var available = values.GetValueOrDefault("MemAvailable");
                var swapTotal = values.GetValueOrDefault("SwapTotal");
                var swapFree = values.GetValueOrDefault("SwapFree");
                return new MemoryData(Math.Max(0, total - available), total, available,
                    Math.Max(0, swapTotal - swapFree), swapTotal);
            }
            catch
            {
                // Fall through to the managed approximation.
            }
        }

        var availableManaged = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        var usedManaged = GC.GetTotalMemory(false);
        var totalManaged = Math.Max(usedManaged, availableManaged);
        return new MemoryData(usedManaged, totalManaged, Math.Max(0, totalManaged - usedManaged), 0, 0);
    }

    private static DiskData ReadDisk()
    {
        long total = 0;
        long free = 0;
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType is DriveType.CDRom or DriveType.Network) continue;
                total = SaturatingAdd(total, drive.TotalSize);
                free = SaturatingAdd(free, drive.AvailableFreeSpace);
            }
            catch
            {
                // A drive may disappear while metrics are being collected.
            }
        }

        return new DiskData(Math.Max(0, total - free), total, free);
    }

    private static (long Received, long Sent) ReadNetwork()
    {
        long received = 0;
        long sent = 0;
        try
        {
            foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (network.OperationalStatus != OperationalStatus.Up || network.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var statistics = network.GetIPStatistics();
                received = SaturatingAdd(received, statistics.BytesReceived);
                sent = SaturatingAdd(sent, statistics.BytesSent);
            }
        }
        catch
        {
            // Network counters are optional on some platforms.
        }

        return (received, sent);
    }

    private (double ReceivedPerSecond, double SentPerSecond) ReadNetworkRates((long Received, long Sent) current, DateTimeOffset collectedAt)
    {
        lock (_sync)
        {
            var previous = _previousNetwork;
            _previousNetwork = new NetworkSample(current.Received, current.Sent, collectedAt);
            if (previous is null) return (0, 0);
            var seconds = (collectedAt - previous.Value.CollectedAt).TotalSeconds;
            if (seconds <= 0) return (0, 0);
            return (
                Math.Max(0, current.Received - previous.Value.Received) / seconds,
                Math.Max(0, current.Sent - previous.Value.Sent) / seconds);
        }
    }

    private double ReadProcessCpuPercent(Process process, DateTimeOffset collectedAt)
    {
        TimeSpan totalProcessorTime;
        try { totalProcessorTime = process.TotalProcessorTime; }
        catch { return 0; }

        lock (_sync)
        {
            var previous = _previousProcessCpu;
            _previousProcessCpu = new ProcessCpuSample(totalProcessorTime, collectedAt);
            if (previous is null) return 0;
            var elapsedMilliseconds = (collectedAt - previous.Value.CollectedAt).TotalMilliseconds;
            var cpuMilliseconds = (totalProcessorTime - previous.Value.TotalProcessorTime).TotalMilliseconds;
            if (elapsedMilliseconds <= 0 || cpuMilliseconds < 0) return 0;
            return Math.Clamp(cpuMilliseconds * 100d / elapsedMilliseconds / Math.Max(1, Environment.ProcessorCount), 0, 100);
        }
    }

    private static int ReadProcessCount()
    {
        try
        {
            var processes = Process.GetProcesses();
            try { return processes.Length; }
            finally { foreach (var process in processes) process.Dispose(); }
        }
        catch { return 0; }
    }

    private static int ReadTcpConnectionCount()
    {
        try { return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections().Length; }
        catch { return 0; }
    }

    private static double SafeRead(Func<double> reader)
    {
        try { return reader(); }
        catch { return 0; }
    }

    private static double Percent(long used, long total) => total <= 0 ? 0 : Math.Clamp(used * 100d / total, 0, 100);

    private static long ParseKilobytes(string value)
    {
        var text = value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return long.TryParse(text, out var kilobytes) ? SaturatingMultiply(kilobytes, 1_024) : 0;
    }

    private static long SafeLong(ulong value) => value > long.MaxValue ? long.MaxValue : (long)value;
    private static long SaturatingAdd(long left, long right) => left > long.MaxValue - right ? long.MaxValue : left + right;
    private static long SaturatingMultiply(long value, long factor) => value > long.MaxValue / factor ? long.MaxValue : value * factor;
    private static ulong ToUInt64(FileTime time) => ((ulong)time.High << 32) | time.Low;

    private readonly record struct CpuTimes(ulong Idle, ulong Total);
    private readonly record struct NetworkSample(long Received, long Sent, DateTimeOffset CollectedAt);
    private readonly record struct ProcessCpuSample(TimeSpan TotalProcessorTime, DateTimeOffset CollectedAt);
    private readonly record struct MemoryData(long Used, long Total, long Available, long PageFileUsed, long PageFileTotal);
    private readonly record struct DiskData(long Used, long Total, long Free);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);
}
