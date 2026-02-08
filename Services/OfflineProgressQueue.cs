using NineLivesAudio.Data;

namespace NineLivesAudio.Services;

public class OfflineProgressQueue : IOfflineProgressQueue
{
    private readonly ILocalDatabase _database;
    private readonly IAudioBookshelfApiService _apiService;
    private readonly IConnectivityService _connectivity;
    private readonly ILoggingService _logger;
    private readonly SemaphoreSlim _drainLock = new(1, 1);

    public OfflineProgressQueue(
        ILocalDatabase database,
        IAudioBookshelfApiService apiService,
        IConnectivityService connectivity,
        ILoggingService logger)
    {
        _database = database;
        _apiService = apiService;
        _connectivity = connectivity;
        _logger = logger;

        // Auto-drain when connectivity returns
        _connectivity.ConnectivityChanged += async (s, e) =>
        {
            if (e.IsServerReachable)
            {
                _logger.Log("[OfflineQueue] Server reachable, draining queue");
                await DrainQueueAsync();
            }
        };
    }

    public async Task EnqueueAsync(string itemId, double currentTime, bool isFinished)
    {
        await _database.EnqueuePendingProgressAsync(itemId, currentTime, isFinished);
        _logger.LogDebug($"[OfflineQueue] Enqueued: {itemId} @ {currentTime:F1}s");
    }

    public async Task DrainQueueAsync()
    {
        if (!_apiService.IsAuthenticated || !_connectivity.IsServerReachable)
            return;

        if (!await _drainLock.WaitAsync(0))
            return;

        try
        {
            var pending = await _database.GetPendingProgressAsync();
            if (pending.Count == 0) return;

            // Deduplicate: keep only the latest entry per item
            var latest = pending
                .GroupBy(p => p.ItemId)
                .Select(g => g.OrderByDescending(p => p.Timestamp).First())
                .ToList();

            int synced = 0;
            foreach (var entry in latest)
            {
                try
                {
                    await _apiService.UpdateProgressAsync(entry.ItemId, entry.CurrentTime, entry.IsFinished);
                    synced++;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug($"[OfflineQueue] Sync failed for {entry.ItemId}: {ex.Message}");
                }
            }

            // Clear all pending entries that were processed
            await _database.ClearPendingProgressAsync();
            _logger.Log($"[OfflineQueue] Drained {synced}/{latest.Count} entries");
        }
        finally
        {
            _drainLock.Release();
        }
    }

    public async Task<int> GetPendingCountAsync()
    {
        return await _database.GetPendingProgressCountAsync();
    }
}
