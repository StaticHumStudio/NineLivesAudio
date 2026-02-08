using NineLivesAudio.Data;
using NineLivesAudio.Helpers;
using NineLivesAudio.Models;
using NineLivesAudio.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace NineLivesAudio.ViewModels;

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

            var recentEntries = await _database.GetRecentlyPlayedAsync(9);

            Lives.Clear();
            bool isFirst = true;
            int idx = 0;
            foreach (var (book, lastPlayedAt) in recentEntries)
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
                    CoverPath = book.CoverPath,
                    LifeIndex = idx,
                    LifeLabel = $"LIFE {ToRoman(idx + 1)}",
                    Weight = book.Duration.TotalHours < 4 ? "LIGHT"
                           : book.Duration.TotalHours < 15 ? "MEDIUM" : "HEAVY",
                    TimeGiven = CosmicCatHelper.FormatListeningTime(book.CurrentTime),
                    HoursListened = book.CurrentTime.TotalHours,
                    LastPlayedAt = lastPlayedAt,
                    LastPlayedLabel = FormatRelativeTime(lastPlayedAt)
                });
                isFirst = false;
                idx++;
            }

            // Compute aggregate listening time across all displayed items
            var totalTime = TimeSpan.FromSeconds(
                recentEntries.Sum(e => e.Book.CurrentTime.TotalSeconds));
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

    private static readonly string[] RomanNumerals =
        { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX" };

    private static string ToRoman(int number) =>
        number >= 1 && number <= RomanNumerals.Length
            ? RomanNumerals[number - 1]
            : number.ToString();

    private static string FormatRelativeTime(DateTime timestamp)
    {
        if (timestamp == DateTime.MinValue) return "";
        var elapsed = DateTime.Now - timestamp;
        if (elapsed.TotalMinutes < 1) return "Just now";
        if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes}m ago";
        if (elapsed.TotalHours < 24) return $"{(int)elapsed.TotalHours}h ago";
        if (elapsed.TotalDays < 2) return "Yesterday";
        if (elapsed.TotalDays < 7) return $"{(int)elapsed.TotalDays}d ago";
        if (elapsed.TotalDays < 30) return $"{(int)(elapsed.TotalDays / 7)}w ago";
        return timestamp.ToString("MMM d");
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
    public double HoursListened { get; set; }
    public string? CoverPath { get; set; }

    // Altar properties
    public int LifeIndex { get; set; }
    public string LifeLabel { get; set; } = string.Empty;
    public string Weight { get; set; } = string.Empty;
    public string TimeGiven { get; set; } = string.Empty;

    // Last listened
    public DateTime LastPlayedAt { get; set; }
    public string LastPlayedLabel { get; set; } = string.Empty;
}
