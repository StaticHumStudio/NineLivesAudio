using NineLivesAudio.Data;

namespace NineLivesAudio.Services;

public class AppInitializer : IAppInitializer
{
    private readonly ILocalDatabase _database;
    private readonly ISettingsService _settingsService;
    private readonly IAudioBookshelfApiService _apiService;
    private readonly ISyncService _syncService;
    private readonly ILoggingService _logger;
    private int _running; // 0 = idle, 1 = running

    public InitState State { get; private set; } = InitState.NotStarted;
    public string? ErrorMessage { get; private set; }
    public event EventHandler<InitState>? StateChanged;

    public AppInitializer(
        ILocalDatabase database,
        ISettingsService settingsService,
        IAudioBookshelfApiService apiService,
        ISyncService syncService,
        ILoggingService loggingService)
    {
        _database = database;
        _settingsService = settingsService;
        _apiService = apiService;
        _syncService = syncService;
        _logger = loggingService;
    }

    public async Task InitializeAsync()
    {
        // Guard: prevent concurrent runs (but allow retry after failure)
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            return;

        if (State == InitState.Ready)
        {
            Interlocked.Exchange(ref _running, 0);
            return; // Already initialized
        }

        try
        {
            SetState(InitState.Initializing);
            _logger.Log("AppInitializer: starting initialization");

            // 1. Database
            _logger.Log("AppInitializer: initializing database...");
            await _database.InitializeAsync();
            _logger.Log("AppInitializer: database ready");

            // 2. Settings
            _logger.Log("AppInitializer: loading settings...");
            await _settingsService.LoadSettingsAsync();
            _logger.Log($"AppInitializer: settings loaded (server={_settingsService.Settings.ServerUrl})");

            // 3. Validate token if we have one
            if (!string.IsNullOrEmpty(_settingsService.Settings.ServerUrl))
            {
                _logger.Log("AppInitializer: validating auth token...");
                var valid = await _apiService.ValidateTokenAsync();
                _logger.Log($"AppInitializer: token valid={valid}");

                // 4. Start sync if authenticated
                if (valid && _settingsService.Settings.AutoSyncProgress)
                {
                    _logger.Log("AppInitializer: starting sync service");
                    await _syncService.StartAsync();
                }
            }

            SetState(InitState.Ready);
            _logger.Log("AppInitializer: initialization complete");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _logger.LogError("AppInitializer: initialization failed", ex);
            SetState(InitState.Failed);
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    private void SetState(InitState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }
}
