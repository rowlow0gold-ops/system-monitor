using System;

namespace SystemMonitor.Models;

public class SystemMetrics
{
    public double CpuUsage { get; set; }
    public double RamUsagePercent { get; set; }
    public double RamUsedGB { get; set; }
    public double RamTotalGB { get; set; }
    public double DiskUsagePercent { get; set; }
    public double DiskUsedGB { get; set; }
    public double DiskTotalGB { get; set; }
    public double NetworkSentKBps { get; set; }
    public double NetworkReceivedKBps { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
}
