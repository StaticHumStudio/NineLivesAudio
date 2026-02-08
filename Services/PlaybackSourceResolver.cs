using NineLivesAudio.Models;

namespace NineLivesAudio.Services;

public interface IPlaybackSourceResolver
{
    /// <summary>
    /// Determines the best playback source for an audiobook.
    /// Priority: Local downloaded file > Stream from server > Error
    /// </summary>
    PlaybackSource ResolveSource(AudioBook audioBook);
}

public enum PlaybackSourceType
{
    LocalFile,
    Stream,
    Unavailable
}

public class PlaybackSource
{
    public PlaybackSourceType Type { get; set; }
    public string? LocalFilePath { get; set; }
    public string? StreamItemId { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsDownloaded => Type == PlaybackSourceType.LocalFile;

    public static PlaybackSource FromLocal(string filePath) => new()
    {
        Type = PlaybackSourceType.LocalFile,
        LocalFilePath = filePath
    };

    public static PlaybackSource FromStream(string itemId) => new()
    {
        Type = PlaybackSourceType.Stream,
        StreamItemId = itemId
    };

    public static PlaybackSource Unavailable(string error) => new()
    {
        Type = PlaybackSourceType.Unavailable,
        ErrorMessage = error
    };
}

public class PlaybackSourceResolver : IPlaybackSourceResolver
{
    private readonly IAudioBookshelfApiService _apiService;
    private readonly ILoggingService _logger;

    public PlaybackSourceResolver(
        IAudioBookshelfApiService apiService,
        ILoggingService loggingService)
    {
        _apiService = apiService;
        _logger = loggingService;
    }

    public PlaybackSource ResolveSource(AudioBook audioBook)
    {
        if (audioBook == null)
        {
            _logger.LogWarning("[PlaybackSourceResolver] AudioBook is null");
            return PlaybackSource.Unavailable("No audiobook provided");
        }

        // Check for local downloaded file first
        var localSource = TryResolveLocalSource(audioBook);
        if (localSource != null)
        {
            _logger.Log($"[PlaybackSourceResolver] Using LOCAL file for '{audioBook.Title}'");
            return localSource;
        }

        // Fall back to streaming
        if (_apiService.IsAuthenticated)
        {
            _logger.Log($"[PlaybackSourceResolver] Using STREAM for '{audioBook.Title}'");
            return PlaybackSource.FromStream(audioBook.Id);
        }

        _logger.LogWarning($"[PlaybackSourceResolver] UNAVAILABLE - not downloaded and not authenticated for '{audioBook.Title}'");
        return PlaybackSource.Unavailable("Book is not downloaded and you're not connected to the server.");
    }

    private PlaybackSource? TryResolveLocalSource(AudioBook audioBook)
    {
        // Quick check: is it marked as downloaded?
        if (!audioBook.IsDownloaded)
            return null;

        // Check if audio files have local paths
        if (audioBook.AudioFiles == null || audioBook.AudioFiles.Count == 0)
            return null;

        // Find the first audio file with a valid local path that exists on disk
        foreach (var audioFile in audioBook.AudioFiles.OrderBy(f => f.Index))
        {
            if (!string.IsNullOrEmpty(audioFile.LocalPath) && File.Exists(audioFile.LocalPath))
            {
                _logger.Log($"[PlaybackSourceResolver] Found valid local file: {audioFile.LocalPath}");
                return PlaybackSource.FromLocal(audioFile.LocalPath);
            }
        }

        // IsDownloaded=true but no valid files — log details for debugging
        var withPath = audioBook.AudioFiles.Count(f => !string.IsNullOrEmpty(f.LocalPath));
        _logger.LogWarning($"[PlaybackSourceResolver] IsDownloaded=true but no valid local files for '{audioBook.Title}'. " +
            $"AudioFiles: {audioBook.AudioFiles.Count}, WithLocalPath: {withPath}");
        if (withPath > 0)
        {
            var firstPath = audioBook.AudioFiles.First(f => !string.IsNullOrEmpty(f.LocalPath)).LocalPath;
            _logger.LogWarning($"[PlaybackSourceResolver] First LocalPath: {firstPath}, Exists: {File.Exists(firstPath!)}");
        }

        return null;
    }
}
