using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace DevCockpit;

public sealed record SystemPulseSnapshot(
    double CpuPercent,
    double MemoryPercent,
    ulong MemoryUsedBytes,
    ulong MemoryTotalBytes,
    double DiskPercent,
    long DiskFreeBytes,
    double DownloadBytesPerSecond,
    double UploadBytesPerSecond,
    long? InternetLatencyMs,
    string LocalAddress,
    string NetworkName,
    string Uptime,
    int ScreenCount,
    string ScreenSummary,
    IReadOnlyList<string> ActiveTools,
    int ProcessCount,
    DateTime CapturedAt)
{
    public static SystemPulseSnapshot Empty { get; } = new(
        0, 0, 0, 0, 0, 0, 0, 0, null, "—", "Нет сети", "—", 0, "—", [], 0, DateTime.Now);
}

public sealed class SystemPulseService
{
    private ulong _lastIdle;
    private ulong _lastKernel;
    private ulong _lastUser;
    private long _lastReceived;
    private long _lastSent;
    private DateTime _lastNetworkAt = DateTime.Now;

    public async Task<SystemPulseSnapshot> CaptureAsync()
    {
        var cpu = ReadCpu();
        var memory = ReadMemory();
        var disk = ReadSystemDrive();
        var network = ReadNetworkRate();
        var connection = ReadNetworkIdentity();
        var latency = await ReadLatencyAsync();
        var screens = Forms.Screen.AllScreens;
        var processes = Process.GetProcesses();
        try
        {
            var activeTools = DetectTools(processes);
            return new SystemPulseSnapshot(
                cpu,
                memory.Percent,
                memory.Used,
                memory.Total,
                disk.Percent,
                disk.Free,
                network.Download,
                network.Upload,
                latency,
                connection.Address,
                connection.Name,
                FormatDuration(TimeSpan.FromMilliseconds(Environment.TickCount64)),
                screens.Length,
                string.Join(" · ", screens.Select(s => $"{s.Bounds.Width}x{s.Bounds.Height}{(s.Primary ? " primary" : "")}")),
                activeTools,
                processes.Length,
                DateTime.Now);
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    private double ReadCpu()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
        var idleValue = ToUInt64(idle);
        var kernelValue = ToUInt64(kernel);
        var userValue = ToUInt64(user);
        if (_lastKernel == 0)
        {
            _lastIdle = idleValue;
            _lastKernel = kernelValue;
            _lastUser = userValue;
            return 0;
        }

        var idleDelta = idleValue - _lastIdle;
        var totalDelta = kernelValue - _lastKernel + userValue - _lastUser;
        _lastIdle = idleValue;
        _lastKernel = kernelValue;
        _lastUser = userValue;
        return totalDelta == 0 ? 0 : Math.Clamp((totalDelta - idleDelta) * 100d / totalDelta, 0, 100);
    }

    private static (double Percent, ulong Used, ulong Total) ReadMemory()
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref status) || status.TotalPhysical == 0) return (0, 0, 0);
        var used = status.TotalPhysical - status.AvailablePhysical;
        return (used * 100d / status.TotalPhysical, used, status.TotalPhysical);
    }

    private static (double Percent, long Free) ReadSystemDrive()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var drive = new DriveInfo(root);
            var used = drive.TotalSize - drive.AvailableFreeSpace;
            return (drive.TotalSize == 0 ? 0 : used * 100d / drive.TotalSize, drive.AvailableFreeSpace);
        }
        catch
        {
            return (0, 0);
        }
    }

    private (double Download, double Upload) ReadNetworkRate()
    {
        long received = 0;
        long sent = 0;
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces().Where(x => x.OperationalStatus == OperationalStatus.Up))
        {
            try
            {
                var stats = adapter.GetIPv4Statistics();
                received += stats.BytesReceived;
                sent += stats.BytesSent;
            }
            catch
            {
                // Some virtual adapters do not expose statistics.
            }
        }

        var now = DateTime.Now;
        var seconds = Math.Max(0.1, (now - _lastNetworkAt).TotalSeconds);
        var download = _lastReceived == 0 ? 0 : Math.Max(0, received - _lastReceived) / seconds;
        var upload = _lastSent == 0 ? 0 : Math.Max(0, sent - _lastSent) / seconds;
        _lastReceived = received;
        _lastSent = sent;
        _lastNetworkAt = now;
        return (download, upload);
    }

    private static (string Address, string Name) ReadNetworkIdentity()
    {
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces()
                     .Where(x => x.OperationalStatus == OperationalStatus.Up && x.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                     .OrderByDescending(x => x.Speed))
        {
            var address = adapter.GetIPProperties().UnicastAddresses
                .FirstOrDefault(x => x.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString();
            if (!string.IsNullOrWhiteSpace(address)) return (address, adapter.Name);
        }
        return ("—", "Нет активной сети");
    }

    private static async Task<long?> ReadLatencyAsync()
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync("1.1.1.1", 900);
            return reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<string> DetectTools(IEnumerable<Process> processes)
    {
        var names = processes.Select(p =>
        {
            try { return p.ProcessName; }
            catch { return ""; }
        }).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var known = new (string Process, string Label)[]
        {
            ("Cursor", "Cursor"), ("Code", "VS Code"), ("1cv8", "1C"),
            ("AnyDesk", "AnyDesk"), ("mstsc", "RDP"), ("WindowsTerminal", "Terminal"),
            ("pwsh", "PowerShell"), ("devenv", "Visual Studio")
        };
        return known.Where(x => names.Contains(x.Process)).Select(x => x.Label).ToList();
    }

    private static string FormatDuration(TimeSpan value) =>
        value.TotalDays >= 1
            ? $"{(int)value.TotalDays} д {value.Hours} ч"
            : $"{value.Hours} ч {value.Minutes} мин";

    private static ulong ToUInt64(FileTime value) => ((ulong)value.High << 32) | value.Low;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }
}
