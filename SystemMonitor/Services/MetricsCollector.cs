using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SystemMonitor.Models;

namespace SystemMonitor.Services;

public class MetricsCollector
{
    private long _lastBytesSent;
    private long _lastBytesReceived;
    private DateTime _lastNetworkCheck = DateTime.MinValue;

    public async Task<SystemMetrics> CollectAsync()
    {
        var metrics = new SystemMetrics
        {
            Timestamp = DateTime.Now
        };

        // CPU
        metrics.CpuUsage = await GetCpuUsageAsync();

        // RAM
        var (ramUsed, ramTotal) = GetRamInfo();
        metrics.RamUsedGB = ramUsed;
        metrics.RamTotalGB = ramTotal;
        metrics.RamUsagePercent = ramTotal > 0 ? (ramUsed / ramTotal) * 100 : 0;

        // Disk
        var (diskUsed, diskTotal) = GetDiskInfo();
        metrics.DiskUsedGB = diskUsed;
        metrics.DiskTotalGB = diskTotal;
        metrics.DiskUsagePercent = diskTotal > 0 ? (diskUsed / diskTotal) * 100 : 0;

        // Network
        var (sent, received) = GetNetworkSpeed();
        metrics.NetworkSentKBps = sent;
        metrics.NetworkReceivedKBps = received;

        return metrics;
    }

    private async Task<double> GetCpuUsageAsync()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = "-c \"top -l 1 -n 0 | grep 'CPU usage' | awk '{print $3}' | tr -d '%'\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                if (process != null)
                {
                    var output = await process.StandardOutput.ReadToEndAsync();
                    await process.WaitForExitAsync();
                    if (double.TryParse(output.Trim(), out var cpu))
                        return cpu;
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "wmic",
                    Arguments = "cpu get loadpercentage /value",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                if (process != null)
                {
                    var output = await process.StandardOutput.ReadToEndAsync();
                    await process.WaitForExitAsync();
                    var line = output.Split('\n').FirstOrDefault(l => l.Contains("LoadPercentage"));
                    if (line != null && double.TryParse(line.Split('=').Last().Trim(), out var cpu))
                        return cpu;
                }
            }
            else // Linux
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = "-c \"grep 'cpu ' /proc/stat | awk '{usage=($2+$4)*100/($2+$4+$5)} END {print usage}'\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                if (process != null)
                {
                    var output = await process.StandardOutput.ReadToEndAsync();
                    await process.WaitForExitAsync();
                    if (double.TryParse(output.Trim(), out var cpu))
                        return cpu;
                }
            }
        }
        catch { }
        return 0;
    }

    private (double usedGB, double totalGB) GetRamInfo()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // Get total RAM
                var psi = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = "-c \"sysctl -n hw.memsize\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                if (process != null)
                {
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    if (long.TryParse(output.Trim(), out var totalBytes))
                    {
                        var totalGB = totalBytes / (1024.0 * 1024 * 1024);

                        // Get VM stats for used memory
                        var psi2 = new ProcessStartInfo
                        {
                            FileName = "/bin/bash",
                            Arguments = "-c \"vm_stat | awk '/Pages active/ {active=$3} /Pages wired/ {wired=$4} END {gsub(/\\./,\\\"\\\",active); gsub(/\\./,\\\"\\\",wired); print (active+wired)*4096}'\"",
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var process2 = Process.Start(psi2);
                        if (process2 != null)
                        {
                            var output2 = process2.StandardOutput.ReadToEnd();
                            process2.WaitForExit();
                            if (long.TryParse(output2.Trim(), out var usedBytes))
                            {
                                return (usedBytes / (1024.0 * 1024 * 1024), totalGB);
                            }
                        }
                        return (totalGB * 0.5, totalGB); // fallback
                    }
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var lines = File.ReadAllLines("/proc/meminfo");
                long total = 0, available = 0;
                foreach (var line in lines)
                {
                    if (line.StartsWith("MemTotal:"))
                        total = long.Parse(line.Split(':')[1].Trim().Split(' ')[0]);
                    if (line.StartsWith("MemAvailable:"))
                        available = long.Parse(line.Split(':')[1].Trim().Split(' ')[0]);
                }
                var totalGB = total / (1024.0 * 1024);
                var usedGB = (total - available) / (1024.0 * 1024);
                return (usedGB, totalGB);
            }
        }
        catch { }
        return (0, 0);
    }

    private (double usedGB, double totalGB) GetDiskInfo()
    {
        try
        {
            var drive = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && d.Name == "/");
            if (drive == null)
                drive = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady);

            if (drive != null)
            {
                var totalGB = drive.TotalSize / (1024.0 * 1024 * 1024);
                var usedGB = (drive.TotalSize - drive.AvailableFreeSpace) / (1024.0 * 1024 * 1024);
                return (usedGB, totalGB);
            }
        }
        catch { }
        return (0, 0);
    }

    private (double sentKBps, double receivedKBps) GetNetworkSpeed()
    {
        try
        {
            var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                    && n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback);

            long totalSent = 0, totalReceived = 0;
            foreach (var ni in interfaces)
            {
                var stats = ni.GetIPStatistics();
                totalSent += stats.BytesSent;
                totalReceived += stats.BytesReceived;
            }

            var now = DateTime.Now;
            double sentKBps = 0, receivedKBps = 0;

            if (_lastNetworkCheck != DateTime.MinValue)
            {
                var elapsed = (now - _lastNetworkCheck).TotalSeconds;
                if (elapsed > 0)
                {
                    sentKBps = (totalSent - _lastBytesSent) / 1024.0 / elapsed;
                    receivedKBps = (totalReceived - _lastBytesReceived) / 1024.0 / elapsed;
                }
            }

            _lastBytesSent = totalSent;
            _lastBytesReceived = totalReceived;
            _lastNetworkCheck = now;

            return (Math.Max(0, sentKBps), Math.Max(0, receivedKBps));
        }
        catch { }
        return (0, 0);
    }
}
