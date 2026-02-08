using NineLivesAudio.Data;
using NineLivesAudio.Models;

namespace NineLivesAudio.Services;

public class SyncService : ISyncService, IDisposable
{
    private readonly IAudioBookshelfApiService _apiService;
    private readonly ILocalDatabase _database;
    private readonly ISettingsService _settingsService;
    private readonly ILoggingService _logger;
    private Timer? _syncTimer;
    private bool _isSyncing;
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    // Progress coordinator state
    private string? _activeItemId;
    private double _lastSyncedTime;
    private DateTime _lastSyncTimestamp = DateTime.MinValue;
    private static readonly TimeSpan MinSyncInterval = TimeSpan.FromSeconds(30);
    private const double MinPositionDelta = 2.0;    // seconds
    private const double MinProgressDelta = 0.01;   // 1%

    public bool IsSyncing => _isSyncing;
    public DateTime? LastSyncTime { get; private set; }

    public event EventHandler<SyncEventArgs>? SyncStarted;
    public event EventHandler<SyncEventArgs>? SyncCompleted;
    public event EventHandler<SyncErrorEventArgs>? SyncFailed;

    public SyncService(
        IAudioBookshelfApiService apiService,
        ILocalDatabase database,
        ISettingsService settingsService,
        ILoggingService loggingService)
    {
        _apiService = apiService;
        _database = database;
        _settingsService = settingsService;
        _logger = loggingService;
    }

    public Task StartAsync()
    {
        if (_syncTimer != null) return Task.CompletedTask;

        var intervalMinutes = _settingsService.Settings.SyncIntervalMinutes;
        var interval = TimeSpan.FromMinutes(intervalMinutes > 0 ? intervalMinutes : 5);

        _syncTimer = new Timer(
            async _ => await SyncNowAsync(),
            null,
            TimeSpan.FromSeconds(3), // Initial delay — fast first sync so Home page gets data quickly
            interval);

        _logger.Log($"[Sync] Started with {interval.TotalMinutes}min interval");
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _syncTimer?.Dispose();
        _syncTimer = null;
        _logger.Log("[Sync] Stopped");
        return Task.CompletedTask;
    }

    public async Task SyncNowAsync()
    {
        if (!_apiService.IsAuthenticated)
        {
            _logger.LogDebug("[Sync] Skipping — not authenticated");
            return;
        }

        if (!await _syncLock.WaitAsync(0))
        {
            _logger.LogDebug("[Sync] Already in progress, skipping");
            return;
        }

        try
        {
            _isSyncing = true;
            SyncStarted?.Invoke(this, new SyncEventArgs());

            await SyncLibrariesAsync();
            await SyncProgressAsync();

            LastSyncTime = DateTime.Now;
            SyncCompleted?.Invoke(this, new SyncEventArgs());

            _logger.Log($"[Sync] Complete at {LastSyncTime:HH:mm:ss}");
        }
        catch (Exception ex)
        {
            _logger.LogError("[Sync] Failed", ex);
            SyncFailed?.Invoke(this, new SyncErrorEventArgs
            {
                ErrorMessage = ex.Message,
                Exception = ex
            });
        }
        finally
        {
            _isSyncing = false;
            _syncLock.Release();
        }
    }

    public async Task SyncLibrariesAsync()
    {
        try
        {
            var libraries = await _apiService.GetLibrariesAsync();
            await _database.SaveLibrariesAsync(libraries);

            foreach (var library in libraries)
            {
                var items = await _apiService.GetLibraryItemsAsync(library.Id);

                // Preserve local state (download info + progress) from existing DB records
                foreach (var item in items)
                {
                    var existingItem = await _database.GetAudioBookAsync(item.Id);
                    if (existingItem != null)
                    {
                        item.IsDownloaded = existingItem.IsDownloaded;
                        item.LocalPath = existingItem.LocalPath;

                        // Preserve progress — server library endpoint doesn't include it
                        if (existingItem.Progress > 0 || existingItem.CurrentTime.TotalSeconds > 0)
                        {
                            item.CurrentTime = existingItem.CurrentTime;
                            item.Progress = existingItem.Progress;
                            item.IsFinished = existingItem.IsFinished;
                        }

                        foreach (var audioFile in item.AudioFiles)
                        {
                            var existingFile = existingItem.AudioFiles.FirstOrDefault(f => f.Ino == audioFile.Ino);
                            if (existingFile != null)
                                audioFile.LocalPath = existingFile.LocalPath;
                        }
                    }
                }

                await _database.SaveAudioBooksAsync(items);

                // Seed PlaybackProgress table from server progress so Nine Lives works
                int seeded = 0;
                foreach (var item in items)
                {
                    if (item.CurrentTime.TotalSeconds <= 0 && item.Progress <= 0) continue;
                    if (_activeItemId != null && item.Id == _activeItemId) continue;

                    // If CurrentTime is 0 but we have Progress, estimate position from duration
                    var position = item.CurrentTime;
                    if (position.TotalSeconds <= 0 && item.Progress > 0 && item.Duration.TotalSeconds > 0)
                    {
                        position = TimeSpan.FromSeconds(item.Progress * item.Duration.TotalSeconds);
                    }

                    await _database.SavePlaybackProgressAsync(
                        item.Id,
                        position,
                        item.IsFinished);
                    seeded++;
                }
                if (seeded > 0)
                    _logger.Log($"[Sync] Seeded {seeded} PlaybackProgress entries from cached library data");
            }

            _logger.Log($"[Sync] Synced {libraries.Count} libraries");
        }
        catch (Exception ex)
        {
            _logger.LogError("[Sync] Library sync error", ex);
            throw;
        }
    }

    public async Task SyncProgressAsync()
    {
        try
        {
            var serverProgress = await _apiService.GetAllUserProgressAsync();
            int synced = 0;

            foreach (var progress in serverProgress)
            {
                var audioBook = await _database.GetAudioBookAsync(progress.LibraryItemId);
                if (audioBook == null) continue;

                // Skip if this is the actively-playing item — session owns progress
                if (_activeItemId != null && progress.LibraryItemId == _activeItemId)
                {
                    _logger.LogDebug($"[Sync] Skipping active item {progress.LibraryItemId} — session owns progress");
                    continue;
                }

                audioBook.CurrentTime = progress.CurrentTime;
                audioBook.Progress = progress.Progress;
                audioBook.IsFinished = progress.IsFinished;

                await _database.UpdateAudioBookAsync(audioBook);

                // Use estimated position from progress*duration if currentTime is 0
                var positionToSave = progress.CurrentTime;
                if (positionToSave.TotalSeconds <= 0 && progress.Progress > 0 && audioBook.Duration.TotalSeconds > 0)
                {
                    positionToSave = TimeSpan.FromSeconds(progress.Progress * audioBook.Duration.TotalSeconds);
                }

                await _database.SavePlaybackProgressAsync(
                    progress.LibraryItemId,
                    positionToSave,
                    progress.IsFinished);
                synced++;
            }

            _logger.Log($"[Sync] Progress synced for {synced} items (from {serverProgress.Count} server entries)");
        }
        catch (Exception ex)
        {
            _logger.LogError("[Sync] Progress sync error", ex);
            throw;
        }
    }

    /// <summary>
    /// Called by playback service to indicate which item is actively playing.
    /// While active, periodic sync skips this item (session owns progress).
    /// </summary>
    public void SetActivePlaybackItem(string? itemId)
    {
        _activeItemId = itemId;
        _lastSyncedTime = 0;
        _lastSyncTimestamp = DateTime.MinValue;

        if (itemId != null)
            _logger.LogDebug($"[Sync] Active playback item: {itemId}");
        else
            _logger.LogDebug("[Sync] Active playback item cleared");
    }

    /// <summary>
    /// Called by playback position timer. Throttles sync: only pushes if
    /// position advanced > 2s since last sync OR > 1% progress, AND at least
    /// 30s since last push.
    /// </summary>
    public async Task ReportPlaybackPositionAsync(string itemId, double currentTime, double duration, bool isFinished = false)
    {
        if (!_settingsService.Settings.AutoSyncProgress) return;
        if (!_apiService.IsAuthenticated) return;
        if (string.IsNullOrEmpty(itemId)) return;

        var now = DateTime.UtcNow;
        var timeSinceLastSync = now - _lastSyncTimestamp;
        var positionDelta = Math.Abs(currentTime - _lastSyncedTime);
        var progressDelta = duration > 0 ? positionDelta / duration : 0;

        // Throttle: must meet both time AND delta thresholds (unless finished)
        if (!isFinished)
        {
            if (timeSinceLastSync < MinSyncInterval) return;
            if (positionDelta < MinPositionDelta && progressDelta < MinProgressDelta) return;
        }

        try
        {
            await _apiService.UpdateProgressAsync(itemId, currentTime, isFinished);

            // Also save locally
            await _database.SavePlaybackProgressAsync(
                itemId,
                TimeSpan.FromSeconds(currentTime),
                isFinished);

            _lastSyncedTime = currentTime;
            _lastSyncTimestamp = now;

            _logger.LogDebug($"[Sync] Progress pushed: {currentTime:F1}s (delta {positionDelta:F1}s)");
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"[Sync] Progress push failed: {ex.Message}");
            // Non-fatal — will retry on next tick
        }
    }

    /// <summary>
    /// Force-push final progress on stop/exit regardless of throttle.
    /// </summary>
    public async Task FlushPlaybackProgressAsync(string itemId, double currentTime, double duration, bool isFinished)
    {
        if (!_apiService.IsAuthenticated) return;

        try
        {
            await _apiService.UpdateProgressAsync(itemId, currentTime, isFinished);
            await _database.SavePlaybackProgressAsync(
                itemId,
                TimeSpan.FromSeconds(currentTime),
                isFinished);

            _logger.Log($"[Sync] Final progress flush: {itemId} @ {currentTime:F1}s (finished={isFinished})");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[Sync] Final progress flush failed: {ex.Message}");
        }

        SetActivePlaybackItem(null);
    }

    public void Dispose()
    {
        _syncTimer?.Dispose();
        _syncLock.Dispose();
    }
}
