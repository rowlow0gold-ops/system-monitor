using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using SystemMonitor.Services;

namespace SystemMonitor.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly MetricsCollector _collector = new();
    private readonly int _maxPoints = 60; // 60 seconds of history
    private System.Threading.Timer? _timer;

    // Current values
    [ObservableProperty] private double _cpuUsage;
    [ObservableProperty] private double _ramUsagePercent;
    [ObservableProperty] private string _ramText = "0 / 0 GB";
    [ObservableProperty] private double _diskUsagePercent;
    [ObservableProperty] private string _diskText = "0 / 0 GB";
    [ObservableProperty] private string _networkSentText = "0 KB/s";
    [ObservableProperty] private string _networkReceivedText = "0 KB/s";

    // Alert
    [ObservableProperty] private string _alertText = "";
    [ObservableProperty] private bool _hasAlert;

    // Chart data
    private readonly ObservableCollection<DateTimePoint> _cpuValues = new();
    private readonly ObservableCollection<DateTimePoint> _ramValues = new();
    private readonly ObservableCollection<DateTimePoint> _netSentValues = new();
    private readonly ObservableCollection<DateTimePoint> _netRecvValues = new();

    public ISeries[] CpuSeries { get; }
    public ISeries[] RamSeries { get; }
    public ISeries[] NetworkSeries { get; }

    public Axis[] TimeAxis { get; } =
    {
        new DateTimeAxis(TimeSpan.FromSeconds(1), date => date.ToString("HH:mm:ss"))
        {
            Name = null,
            LabelsRotation = 0,
            LabelsPaint = new SolidColorPaint(SKColors.Gray),
            SeparatorsPaint = new SolidColorPaint(new SKColor(40, 40, 40)),
            AnimationsSpeed = TimeSpan.FromMilliseconds(0)
        }
    };

    public Axis[] CpuYAxis { get; } =
    {
        new Axis
        {
            Name = "CPU %",
            MinLimit = 0,
            MaxLimit = 100,
            NamePaint = new SolidColorPaint(SKColors.DeepSkyBlue),
            LabelsPaint = new SolidColorPaint(SKColors.Gray),
            SeparatorsPaint = new SolidColorPaint(new SKColor(40, 40, 40))
        }
    };

    public Axis[] RamYAxis { get; } =
    {
        new Axis
        {
            Name = "RAM %",
            MinLimit = 0,
            MaxLimit = 100,
            NamePaint = new SolidColorPaint(SKColors.MediumPurple),
            LabelsPaint = new SolidColorPaint(SKColors.Gray),
            SeparatorsPaint = new SolidColorPaint(new SKColor(40, 40, 40))
        }
    };

    public Axis[] NetworkYAxis { get; } =
    {
        new Axis
        {
            Name = "KB/s",
            MinLimit = 0,
            NamePaint = new SolidColorPaint(SKColors.MediumSeaGreen),
            LabelsPaint = new SolidColorPaint(SKColors.Gray),
            SeparatorsPaint = new SolidColorPaint(new SKColor(40, 40, 40))
        }
    };

    public MainWindowViewModel()
    {
        CpuSeries = new ISeries[]
        {
            new LineSeries<DateTimePoint>
            {
                Values = _cpuValues,
                Fill = new SolidColorPaint(new SKColor(0, 191, 255, 40)),
                Stroke = new SolidColorPaint(SKColors.DeepSkyBlue) { StrokeThickness = 2 },
                GeometryFill = null,
                GeometryStroke = null,
                LineSmoothness = 0.3,
                AnimationsSpeed = TimeSpan.FromMilliseconds(0)
            }
        };

        RamSeries = new ISeries[]
        {
            new LineSeries<DateTimePoint>
            {
                Values = _ramValues,
                Fill = new SolidColorPaint(new SKColor(147, 112, 219, 40)),
                Stroke = new SolidColorPaint(SKColors.MediumPurple) { StrokeThickness = 2 },
                GeometryFill = null,
                GeometryStroke = null,
                LineSmoothness = 0.3,
                AnimationsSpeed = TimeSpan.FromMilliseconds(0)
            }
        };

        NetworkSeries = new ISeries[]
        {
            new LineSeries<DateTimePoint>
            {
                Values = _netSentValues,
                Fill = null,
                Stroke = new SolidColorPaint(SKColors.Orange) { StrokeThickness = 2 },
                GeometryFill = null,
                GeometryStroke = null,
                LineSmoothness = 0.3,
                AnimationsSpeed = TimeSpan.FromMilliseconds(0),
                Name = "Sent"
            },
            new LineSeries<DateTimePoint>
            {
                Values = _netRecvValues,
                Fill = null,
                Stroke = new SolidColorPaint(SKColors.MediumSeaGreen) { StrokeThickness = 2 },
                GeometryFill = null,
                GeometryStroke = null,
                LineSmoothness = 0.3,
                AnimationsSpeed = TimeSpan.FromMilliseconds(0),
                Name = "Received"
            }
        };

        StartMonitoring();
    }

    private void StartMonitoring()
    {
        _timer = new System.Threading.Timer(async _ =>
        {
            try
            {
                var m = await _collector.CollectAsync();

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    // Update current values
                    CpuUsage = Math.Round(m.CpuUsage, 1);
                    RamUsagePercent = Math.Round(m.RamUsagePercent, 1);
                    RamText = $"{m.RamUsedGB:F1} / {m.RamTotalGB:F1} GB";
                    DiskUsagePercent = Math.Round(m.DiskUsagePercent, 1);
                    DiskText = $"{m.DiskUsedGB:F1} / {m.DiskTotalGB:F1} GB";
                    NetworkSentText = FormatSpeed(m.NetworkSentKBps);
                    NetworkReceivedText = FormatSpeed(m.NetworkReceivedKBps);

                    // Update charts
                    var point = new DateTimePoint(m.Timestamp, m.CpuUsage);
                    _cpuValues.Add(point);
                    _ramValues.Add(new DateTimePoint(m.Timestamp, m.RamUsagePercent));
                    _netSentValues.Add(new DateTimePoint(m.Timestamp, m.NetworkSentKBps));
                    _netRecvValues.Add(new DateTimePoint(m.Timestamp, m.NetworkReceivedKBps));

                    // Keep max points
                    while (_cpuValues.Count > _maxPoints) _cpuValues.RemoveAt(0);
                    while (_ramValues.Count > _maxPoints) _ramValues.RemoveAt(0);
                    while (_netSentValues.Count > _maxPoints) _netSentValues.RemoveAt(0);
                    while (_netRecvValues.Count > _maxPoints) _netRecvValues.RemoveAt(0);

                    // Alerts
                    CheckAlerts(m.CpuUsage, m.RamUsagePercent, m.DiskUsagePercent);
                });
            }
            catch { }
        }, null, 0, 1000); // every 1 second
    }

    private void CheckAlerts(double cpu, double ram, double disk)
    {
        var alerts = new List<string>();
        if (cpu > 90) alerts.Add($"CPU {cpu:F0}%");
        if (ram > 90) alerts.Add($"RAM {ram:F0}%");
        if (disk > 95) alerts.Add($"Disk {disk:F0}%");

        if (alerts.Count > 0)
        {
            AlertText = $"⚠ HIGH: {string.Join(" | ", alerts)}";
            HasAlert = true;
        }
        else
        {
            AlertText = "";
            HasAlert = false;
        }
    }

    private string FormatSpeed(double kbps)
    {
        if (kbps >= 1024)
            return $"{kbps / 1024:F1} MB/s";
        return $"{kbps:F1} KB/s";
    }
}
