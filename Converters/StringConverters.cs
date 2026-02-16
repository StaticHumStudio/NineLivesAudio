using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace NineLivesAudio.Helpers;

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
