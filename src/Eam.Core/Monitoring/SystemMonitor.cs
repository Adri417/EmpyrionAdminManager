using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Eam.Core.Monitoring;

public sealed record DiskInfo(string Name, long TotalBytes, long FreeBytes);

public sealed record SystemSnapshot(
    DateTime TimeUtc,
    double CpuPercent,
    long MemoryTotalBytes,
    long MemoryUsedBytes,
    long EamMemoryBytes,
    IReadOnlyList<DiskInfo> Disks,
    TimeSpan MachineUptime,
    TimeSpan EamUptime,
    string MachineName,
    string OsDescription,
    int ProcessorCount);

/// <summary>
/// Reads CPU, memory and disks without any extra package or performance counter
/// (works on Windows, and on Linux for development).
/// </summary>
public sealed class SystemMonitor
{
    private readonly object _lock = new();
    private (ulong idle, ulong total)? _lastCpu;
    private double _lastCpuPercent;
    private static readonly DateTime StartedUtc = Process.GetCurrentProcess().StartTime.ToUniversalTime();

    public SystemSnapshot Take()
    {
        double cpu;
        lock (_lock)
        {
            var now = ReadCpuTimes();
            if (now is { } n && _lastCpu is { } prev && n.total > prev.total)
            {
                var total = n.total - prev.total;
                var idle = n.idle - prev.idle;
                _lastCpuPercent = Math.Clamp(100.0 * (total - idle) / total, 0, 100);
            }
            if (now is not null) _lastCpu = now;
            cpu = Math.Round(_lastCpuPercent, 1);
        }

        var (memTotal, memAvail) = ReadMemory();
        using var self = Process.GetCurrentProcess();

        var disks = new List<DiskInfo>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady || d.DriveType != DriveType.Fixed || d.TotalSize < 1L << 30) continue;
                if (!OperatingSystem.IsWindows() && d.Name != "/" && !d.Name.StartsWith("/mnt") && !d.Name.StartsWith("/home")) continue;
                disks.Add(new DiskInfo(d.Name, d.TotalSize, d.AvailableFreeSpace));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return new SystemSnapshot(
            DateTime.UtcNow, cpu, memTotal, Math.Max(0, memTotal - memAvail), self.WorkingSet64, disks,
            TimeSpan.FromMilliseconds(Environment.TickCount64), DateTime.UtcNow - StartedUtc,
            Environment.MachineName, RuntimeInformation.OSDescription, Environment.ProcessorCount);
    }

    private static (ulong idle, ulong total)? ReadCpuTimes()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                if (!GetSystemTimes(out var idle, out var kernel, out var user)) return null;
                // Kernel time already includes idle time.
                return (idle.Value, kernel.Value + user.Value);
            }
            if (File.Exists("/proc/stat"))
            {
                var line = File.ReadLines("/proc/stat").First();
                var v = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(ulong.Parse).ToArray();
                var idleAll = v[3] + (v.Length > 4 ? v[4] : 0);
                ulong total = 0;
                foreach (var x in v.Take(8)) total += x;
                return (idleAll, total);
            }
        }
        catch { }
        return null;
    }

    private static (long total, long available) ReadMemory()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var s = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
                if (GlobalMemoryStatusEx(ref s)) return ((long)s.ullTotalPhys, (long)s.ullAvailPhys);
            }
            else if (File.Exists("/proc/meminfo"))
            {
                long total = 0, avail = 0;
                foreach (var l in File.ReadLines("/proc/meminfo"))
                {
                    if (l.StartsWith("MemTotal:")) total = ParseKb(l);
                    else if (l.StartsWith("MemAvailable:")) avail = ParseKb(l);
                }
                return (total, avail);
            }
        }
        catch { }
        var gc = GC.GetGCMemoryInfo();
        return (gc.TotalAvailableMemoryBytes, gc.TotalAvailableMemoryBytes);
    }

    private static long ParseKb(string line) =>
        long.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]) * 1024;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
        public readonly ulong Value => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
