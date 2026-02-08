using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace AudioBookshelfApp.Helpers;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is Visibility visibility)
        {
            return visibility == Visibility.Visible;
        }
        return false;
    }
}

public class BoolToVisibilityInverseConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Visibility.Collapsed : Visibility.Visible;
        }
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is Visibility visibility)
        {
            return visibility == Visibility.Collapsed;
        }
        return true;
    }
}

public class BoolToInverseConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return false;
    }
}

public class ProgressToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is double progress)
        {
            return progress > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class TimeSpanToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is TimeSpan ts)
        {
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
        }
        return "00:00";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value != null ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class NullToVisibilityInverseConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value == null ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class DownloadStatusToGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is Models.DownloadStatus status)
        {
            return status switch
            {
                Models.DownloadStatus.Queued => "\uE896",
                Models.DownloadStatus.Downloading => "\uE769",
                Models.DownloadStatus.Paused => "\uE768",
                Models.DownloadStatus.Completed => "\uE73E",
                Models.DownloadStatus.Failed => "\uE783",
                Models.DownloadStatus.Cancelled => "\uE711",
                _ => "\uE896"
            };
        }
        return "\uE896";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class StringToImageSourceConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string url && !string.IsNullOrEmpty(url))
        {
            try
            {
                return new BitmapImage(new Uri(url));
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class HoursToCosmicBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush DefaultBrush =
        new(Windows.UI.Color.FromArgb(0xFF, 0x4A, 0x4A, 0x4A));

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is double hours)
        {
            var color = CosmicCatHelper.GetCosmicEnergyColor(hours);
            return new SolidColorBrush(color);
        }
        return DefaultBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a duration string (e.g. "LIGHT", "MEDIUM", "HEAVY") to a themed brush.
/// LIGHT → MistFaint, MEDIUM → Mist, HEAVY → SigilGoldDim
/// </summary>
public class WeightToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush LightBrush =
        new(Windows.UI.Color.FromArgb(0xFF, 0x6B, 0x72, 0x80));   // MistFaint
    private static readonly SolidColorBrush MediumBrush =
        new(Windows.UI.Color.FromArgb(0xFF, 0x9B, 0xA4, 0xB5));   // Mist
    private static readonly SolidColorBrush HeavyBrush =
        new(Windows.UI.Color.FromArgb(0xFF, 0x8A, 0x73, 0x39));   // SigilGoldDim

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string weight)
        {
            return weight switch
            {
                "HEAVY" => HeavyBrush,
                "MEDIUM" => MediumBrush,
                _ => LightBrush,
            };
        }
        return LightBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a life index (0-8) to a Roman numeral label: "LIFE I" through "LIFE IX".
/// </summary>
public class LifeIndexToLabelConverter : IValueConverter
{
    private static readonly string[] RomanNumerals =
        { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX" };

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int index && index >= 0 && index < RomanNumerals.Length)
        {
            return $"LIFE {RomanNumerals[index]}";
        }
        return "LIFE";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a TimeSpan duration to a weight category string.
/// &lt;4h = "LIGHT", 4-15h = "MEDIUM", &gt;15h = "HEAVY"
/// </summary>
public class DurationToWeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is TimeSpan duration)
        {
            if (duration.TotalHours < 4) return "LIGHT";
            if (duration.TotalHours < 15) return "MEDIUM";
            return "HEAVY";
        }
        return "LIGHT";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class BoolToHighlightBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush ActiveBrush =
        new(Windows.UI.Color.FromArgb(0xFF, 0x11, 0x18, 0x27)); // VoidSurface
    private static readonly SolidColorBrush InactiveBrush =
        new(Windows.UI.Color.FromArgb(0x00, 0x00, 0x00, 0x00)); // Transparent

    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? ActiveBrush : InactiveBrush;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
