namespace NineLivesAudio.Helpers;

/// <summary>
/// Shared time formatting utilities used across player, book detail, and mini player views.
/// </summary>
public static class TimeFormatHelper
{
    /// <summary>
    /// Formats a TimeSpan as a clock display: "HH:MM:SS" or "MM:SS".
    /// Used by player views for current/remaining time display.
    /// </summary>
    public static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
    }

    /// <summary>
    /// Formats a TimeSpan as human-readable duration: "Xh Ym" or "Xm Ys".
    /// Used for book and chapter duration display.
    /// </summary>
    public static string FormatDuration(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        return $"{ts.Minutes}m {ts.Seconds}s";
    }

    /// <summary>
    /// Formats bytes as human-readable file size: "X.X GB", "X.X MB", "X.X KB", or "X bytes".
    /// </summary>
    public static string FormatSize(long bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:F1} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} bytes";
    }
}
