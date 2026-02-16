using Microsoft.UI.Xaml.Data;

namespace NineLivesAudio.Helpers;

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
