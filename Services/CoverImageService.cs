using Microsoft.UI.Xaml.Media.Imaging;

namespace NineLivesAudio.Services;

/// <summary>
/// Centralized cover image loading to eliminate duplicate try/catch BitmapImage patterns
/// scattered across BookCard, MiniPlayer, PlayerPage, BookDetailPage, and MainWindow.
/// </summary>
public static class CoverImageService
{
    /// <summary>
    /// Loads a BitmapImage from a cover path URI, returning null on failure.
    /// </summary>
    public static BitmapImage? LoadCover(string? coverPath)
    {
        if (string.IsNullOrEmpty(coverPath))
            return null;

        try
        {
            return new BitmapImage(new Uri(coverPath));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Loads a thumbnail-sized BitmapImage by appending width/height query parameters.
    /// Used for mini player and other compact views.
    /// </summary>
    public static BitmapImage? LoadThumbnail(string? coverPath, int width = 100, int height = 100)
    {
        if (string.IsNullOrEmpty(coverPath))
            return null;

        try
        {
            var thumbnailUri = coverPath.Contains('?')
                ? $"{coverPath}&width={width}&height={height}"
                : $"{coverPath}?width={width}&height={height}";
            return new BitmapImage(new Uri(thumbnailUri));
        }
        catch
        {
            return null;
        }
    }
}
