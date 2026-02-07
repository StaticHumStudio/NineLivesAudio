using AudioBookshelfApp.Data;
using AudioBookshelfApp.Helpers;
using AudioBookshelfApp.Models;
using AudioBookshelfApp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace AudioBookshelfApp.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly ILocalDatabase _database;
    private readonly IAudioPlaybackService _playbackService;
    private readonly ISyncService _syncService;
    private readonly ILoggingService _logger;
    private readonly IMetadataNormalizer _normalizer;
    private Microsoft.UI.Dispatching.DispatcherQueue? _dispatcherQueue;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _showEmptyState;

    [ObservableProperty]
    private string _totalListeningTimeText = "";

    public ObservableCollection<NineLivesItem> Lives { get; } = new();

    public HomeViewModel(
        ILocalDatabase database,
        IAudioPlaybackService playbackService,
        ISyncService syncService,
        ILoggingService loggingService,
        IMetadataNormalizer normalizer)
    {
        _database = database;
        _playbackService = playbackService;
        _syncService = syncService;
        _logger = loggingService;
        _normalizer = normalizer;

        // Reload Nine Lives whenever a sync completes (progress data may have changed)
        _syncService.SyncCompleted += OnSyncCompleted;
    }

    private void OnSyncCompleted(object? sender, SyncEventArgs e)
    {
        _logger.Log("[Home] Sync completed, reloading Nine Lives...");
        _dispatcherQueue?.TryEnqueue(() => _ = LoadAsync());
    }

    public async Task LoadAsync()
    {
        // Capture dispatcher queue on first call (always from UI thread)
        _dispatcherQueue ??= Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        try
        {
            IsLoading = true;
            _logger.Log("[Home] Loading Nine Lives...");

            var recentBooks = await _database.GetRecentlyPlayedAsync(9);

            Lives.Clear();
            bool isFirst = true;
            foreach (var book in recentBooks)
            {
                var normalized = _normalizer.Normalize(book);
                Lives.Add(new NineLivesItem
                {
                    AudioBook = book,
                    DisplayTitle = normalized.DisplayTitle,
                    DisplayAuthor = normalized.DisplayAuthor,
                    ProgressPercent = book.ProgressPercent,
                    IsMostRecent = isFirst,
                    IsDownloaded = book.IsDownloaded,
                    ListeningTimeText = CosmicCatHelper.FormatListeningTime(book.CurrentTime),
                    HoursListened = book.CurrentTime.TotalHours
                });
                isFirst = false;
            }

            // Compute aggregate listening time across all displayed items
            var totalTime = TimeSpan.FromSeconds(
                recentBooks.Sum(b => b.CurrentTime.TotalSeconds));
            TotalListeningTimeText = CosmicCatHelper.FormatListeningTime(totalTime);

            ShowEmptyState = Lives.Count == 0;
            _logger.Log($"[Home] Loaded {Lives.Count} lives, total listened: {TotalListeningTimeText}");
        }
        catch (Exception ex)
        {
            _logger.LogError("[Home] Load failed", ex);
            ShowEmptyState = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task PlayBookAsync(NineLivesItem? item)
    {
        if (item?.AudioBook == null) return;

        try
        {
            _logger.Log($"[Home] Resuming: {item.DisplayTitle}");
            var loaded = await _playbackService.LoadAudioBookAsync(item.AudioBook);
            if (loaded)
            {
                await _playbackService.PlayAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("[Home] Play failed", ex);
        }
    }
}

public class NineLivesItem
{
    public AudioBook AudioBook { get; set; } = null!;
    public string DisplayTitle { get; set; } = string.Empty;
    public string DisplayAuthor { get; set; } = string.Empty;
    public double ProgressPercent { get; set; }
    public bool IsMostRecent { get; set; }
    public bool IsDownloaded { get; set; }
    public string ListeningTimeText { get; set; } = string.Empty;
    public double HoursListened { get; set; }
}
