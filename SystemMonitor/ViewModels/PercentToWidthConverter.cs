using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace SystemMonitor.ViewModels;

public class PercentToWidthConverter : IValueConverter
{
    public static readonly PercentToWidthConverter Instance = new();
    private const double MaxWidth = 300;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double percent)
            return Math.Max(0, Math.Min(MaxWidth, percent / 100.0 * MaxWidth));
        return 0.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
