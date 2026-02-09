using NineLivesAudio.Models;
using NAudio.Wave;

namespace NineLivesAudio.Services;

public class AudioPlaybackService : IAudioPlaybackService, IDisposable
{
    private readonly IAudioBookshelfApiService _apiService;
    private readonly ISettingsService _settingsService;
    private readonly ILoggingService _logger;
    private readonly IPlaybackSourceResolver _sourceResolver;
    private readonly INotificationService _notifications;
    private readonly ISyncService _syncService;
    private readonly Data.ILocalDatabase _database;

    // NAudio for local file playback
    private WaveOutEvent? _outputDevice;
    private AudioFileReader? _audioFileReader;

    // Windows MediaPlayer for streaming
    private Windows.Media.Playback.MediaPlayer? _mediaPlayer;

    // SMTC: background MediaPlayer used solely for SMTC registration when using NAudio
    private Windows.Media.Playback.MediaPlayer? _smtcPlayer;

    private System.Timers.Timer? _positionTimer;
    private AudioBook? _currentAudioBook;
    private PlaybackSessionInfo? _currentSession;
    private PlaybackState _state = PlaybackState.Stopped;
    private float _playbackSpeed = 1.0f;
    private float _volume = 0.8f;
    private bool _isStreaming; // true = MediaPlayer, false = NAudio local
    private bool _isLocalFile; // true if playing from downloaded file
    private CancellationTokenSource? _loadCts;

    // Session sync timer (12-second interval for progress sync to server)
    private PeriodicTimer? _sessionSyncTimer;
    private CancellationTokenSource? _sessionSyncCts;
    private DateTime _sessionStartTime;
    private double _accumulatedListenTime;

    // Multi-track state
    private List<string> _trackPaths = new();     // local file paths for multi-track
    private List<AudioStreamInfo> _streamTracks = new(); // streaming tracks
    private List<double> _trackDurations = new(); // cumulative durations for offset calc
    private int _currentTrackIndex;
    private bool _autoAdvancing; // guards against re-entrancy during track advance

    // Chapter state
    private List<Chapter> _chapters = new();
    private int _currentChapterIndex = -1;

    public event EventHandler<PlaybackStateChangedEventArgs>? PlaybackStateChanged;
    public event EventHandler<TimeSpan>? PositionChanged;
    public event EventHandler<int>? TrackChanged;
    public event EventHandler<Chapter?>? ChapterChanged;

    public PlaybackState State => _state;
    public float PlaybackSpeed => _playbackSpeed;
    public float Volume => _volume;
    public AudioBook? CurrentAudioBook => _currentAudioBook;
    public int CurrentTrackIndex => _currentTrackIndex;
    public int TotalTracks => Math.Max(_trackPaths.Count, _streamTracks.Count);
    public List<Chapter> Chapters => _chapters;
    public Chapter? CurrentChapter => _currentChapterIndex >= 0 && _currentChapterIndex < _chapters.Count
        ? _chapters[_currentChapterIndex] : null;
    public int CurrentChapterIndex => _currentChapterIndex;

    public TimeSpan Position
    {
        get
        {
            var trackPosition = TimeSpan.Zero;
            if (_isStreaming && _mediaPlayer != null)
                trackPosition = _mediaPlayer.PlaybackSession.Position;
            else
                trackPosition = _audioFileReader?.CurrentTime ?? TimeSpan.Zero;

            // Add cumulative duration of previous tracks
            if (_currentTrackIndex > 0 && _currentTrackIndex <= _trackDurations.Count)
                return trackPosition + TimeSpan.FromSeconds(_trackDurations[_currentTrackIndex - 1]);
            return trackPosition;
        }
    }

    public TimeSpan Duration
    {
        get
        {
            // Total duration across all tracks
            if (_trackDurations.Count > 0)
                return TimeSpan.FromSeconds(_trackDurations.Last());
            if (_isStreaming && _mediaPlayer != null)
                return _mediaPlayer.PlaybackSession.NaturalDuration;
            return _audioFileReader?.TotalTime ?? TimeSpan.Zero;
        }
    }

    public bool IsPlayingLocalFile => _isLocalFile;

    public AudioPlaybackService(
        IAudioBookshelfApiService apiService,
        ISettingsService settingsService,
        ILoggingService loggingService,
        IPlaybackSourceResolver sourceResolver,
        INotificationService notifications,
        ISyncService syncService,
        Data.ILocalDatabase database)
    {
        _apiService = apiService;
        _settingsService = settingsService;
        _logger = loggingService;
        _sourceResolver = sourceResolver;
        _notifications = notifications;
        _syncService = syncService;
        _database = database;

        _positionTimer = new System.Timers.Timer(500);
        _positionTimer.Elapsed += (s, e) =>
        {
            PositionChanged?.Invoke(this, Position);
            UpdateCurrentChapter();
        };
    }

    public async Task<bool> LoadAudioBookAsync(AudioBook audioBook)
    {
        // Cancel any previous load
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        try
        {
            SetState(PlaybackState.Loading);
            _logger.Log($"[Playback] Loading: {audioBook.Title}");

            await StopAsync();
            _currentAudioBook = audioBook;
            _isLocalFile = false;
            _currentTrackIndex = 0;
            _trackPaths.Clear();
            _streamTracks.Clear();
            _trackDurations.Clear();
            _autoAdvancing = false;
            _chapters = audioBook.Chapters.OrderBy(c => c.Start).ToList();
            _currentChapterIndex = -1;

            // === Use PlaybackSourceResolver for decision ===
            var source = _sourceResolver.ResolveSource(audioBook);

            // If the resolver recovered download state from disk (DB was stale),
            // persist it immediately so future loads don't need to re-scan
            if (source.WasRecoveredFromDisk)
            {
                _logger.Log($"[Playback] Persisting recovered download state for '{audioBook.Title}'");
                try { await _database.UpdateAudioBookAsync(audioBook); }
                catch (Exception ex) { _logger.LogDebug($"[Playback] Failed to persist recovered state: {ex.Message}"); }
            }

            // Start a server session — source-type-aware to avoid blocking local playback
            if (_apiService.IsAuthenticated)
            {
                if (source.Type == PlaybackSourceType.LocalFile)
                {
                    // Fire-and-forget with 2s timeout — don't block local file playback
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                            var sessionTask = _apiService.StartPlaybackSessionAsync(audioBook.Id);
                            var completed = await Task.WhenAny(sessionTask, Task.Delay(2000, cts.Token));
                            if (completed == sessionTask && sessionTask.Result != null)
                            {
                                _currentSession = sessionTask.Result;
                                _logger.Log($"[Playback] Session started (background): {_currentSession.Id}");
                                if (_chapters.Count == 0 && _currentSession.Chapters.Count > 0)
                                    _chapters = _currentSession.Chapters.OrderBy(c => c.Start).ToList();
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug($"[Playback] Background session start failed (non-fatal): {ex.Message}");
                        }
                    });
                }
                else
                {
                    // Blocking session start for streams — required for stream URLs
                    try
                    {
                        _currentSession = await _apiService.StartPlaybackSessionAsync(audioBook.Id);
                        if (_currentSession != null)
                        {
                            _logger.Log($"[Playback] Session started: {_currentSession.Id}");
                            if (_chapters.Count == 0 && _currentSession.Chapters.Count > 0)
                                _chapters = _currentSession.Chapters.OrderBy(c => c.Start).ToList();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug($"[Playback] Session start failed (non-fatal): {ex.Message}");
                    }
                }
            }

            // Tell SyncService this item is active (session owns progress)
            _syncService.SetActivePlaybackItem(audioBook.Id);

            // Seed PlaybackProgress so Nine Lives shows this book immediately
            try
            {
                await _database.SavePlaybackProgressAsync(
                    audioBook.Id,
                    audioBook.CurrentTime,
                    isFinished: false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[Playback] Initial progress seed failed (non-fatal): {ex.Message}");
            }

            switch (source.Type)
            {
                case PlaybackSourceType.LocalFile:
                    _logger.Log($"[Playback] Pipeline: LOCAL file {source.LocalFilePath}");
                    _isLocalFile = true;
                    BuildLocalTrackList(audioBook, source.LocalFilePath!);
                    DetermineStartingTrack(audioBook.CurrentTime);
                    var localLoaded = await LoadLocalFileAsync(_trackPaths[_currentTrackIndex], audioBook, ct);
                    if (localLoaded) SetupSmtc(audioBook);
                    return localLoaded;

                case PlaybackSourceType.Stream:
                    _logger.Log($"[Playback] Pipeline: STREAM for {audioBook.Id}");

                    if (_currentSession != null && _currentSession.AudioTracks.Count > 0)
                    {
                        _streamTracks = _currentSession.AudioTracks.OrderBy(t => t.Index).ToList();
                        BuildStreamTrackDurations();

                        var track = _streamTracks[_currentTrackIndex];
                        _logger.Log($"[Playback] Stream URL obtained, {_streamTracks.Count} tracks");

                        if (await LoadStreamAsync(track.ContentUrl, audioBook, ct))
                        {
                            SetupSmtc(audioBook);
                            return true;
                        }

                        _logger.LogWarning("[Playback] Stream failed, falling back to temp download");
                    }

                    _logger.Log("[Playback] Pipeline: FALLBACK temp download");
                    if (_currentSession == null || _currentSession.AudioTracks.Count == 0)
                    {
                        _logger.LogWarning("[Playback] No playback session or tracks available");
                        SetState(PlaybackState.Stopped, "No audio tracks available");
                        _notifications.ShowError("No audio tracks available", "Playback Error");
                        return false;
                    }

                    var fallbackTrack = _currentSession.AudioTracks.First();
                    var fallbackLoaded = await LoadTempDownloadAsync(fallbackTrack.ContentUrl, audioBook, ct);
                    if (fallbackLoaded) SetupSmtc(audioBook);
                    return fallbackLoaded;

                case PlaybackSourceType.Unavailable:
                default:
                    _logger.LogWarning($"[Playback] Source unavailable: {source.ErrorMessage}");
                    SetState(PlaybackState.Stopped, source.ErrorMessage);
                    _notifications.ShowWarning(source.ErrorMessage ?? "Cannot play this book", "Playback");
                    return false;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Log("[Playback] Load cancelled");
            SetState(PlaybackState.Stopped);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError("[Playback] Load failed", ex);
            SetState(PlaybackState.Stopped, $"Failed to load: {ex.Message}");
            _notifications.ShowError(ex.Message, "Playback Error");
            return false;
        }
    }

    private Task<bool> LoadLocalFileAsync(string path, AudioBook audioBook, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _isStreaming = false;

        // Dispose previous NAudio objects
        _outputDevice?.Stop();
        _outputDevice?.Dispose();
        _audioFileReader?.Dispose();

        _audioFileReader = new AudioFileReader(path);
        _outputDevice = new WaveOutEvent();
        _outputDevice.Init(_audioFileReader);
        _outputDevice.Volume = _volume;
        WireNAudioEvents();

        // Seek within the current track (not overall position)
        var withinTrackOffset = GetWithinTrackOffset(audioBook.CurrentTime);
        if (withinTrackOffset.TotalSeconds > 0)
            _audioFileReader.CurrentTime = withinTrackOffset;

        SetState(PlaybackState.Paused);
        _logger.Log($"[Playback] Track {_currentTrackIndex + 1}/{TotalTracks} loaded. Duration: {Duration}");
        return Task.FromResult(true);
    }

    private Task<bool> LoadLocalTrackAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _isStreaming = false;

        _outputDevice?.Stop();
        _outputDevice?.Dispose();
        _audioFileReader?.Dispose();

        _audioFileReader = new AudioFileReader(path);
        _outputDevice = new WaveOutEvent();
        _outputDevice.Init(_audioFileReader);
        _outputDevice.Volume = _volume;
        WireNAudioEvents();

        SetState(PlaybackState.Playing);
        _outputDevice.Play();
        _positionTimer?.Start();

        _logger.Log($"[Playback] Track {_currentTrackIndex + 1}/{TotalTracks} loaded (auto-advance)");
        return Task.FromResult(true);
    }

    private async Task<bool> LoadStreamAsync(string streamUrl, AudioBook audioBook, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            _isStreaming = true;

            _mediaPlayer = new Windows.Media.Playback.MediaPlayer();
            _mediaPlayer.Volume = _volume;
            _mediaPlayer.PlaybackSession.PlaybackRate = _playbackSpeed;
            _mediaPlayer.AutoPlay = false;

            _mediaPlayer.Source = Windows.Media.Core.MediaSource.CreateFromUri(new Uri(streamUrl));

            // Wait for media to open (with timeout)
            var tcs = new TaskCompletionSource<bool>();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));

            void onOpened(Windows.Media.Playback.MediaPlayer sender, object args)
                => tcs.TrySetResult(true);
            void onFailed(Windows.Media.Playback.MediaPlayer sender, Windows.Media.Playback.MediaPlayerFailedEventArgs args)
                => tcs.TrySetResult(false);

            _mediaPlayer.MediaOpened += onOpened;
            _mediaPlayer.MediaFailed += onFailed;

            using (timeoutCts.Token.Register(() => tcs.TrySetResult(false)))
            {
                var opened = await tcs.Task;
                _mediaPlayer.MediaOpened -= onOpened;
                _mediaPlayer.MediaFailed -= onFailed;

                if (!opened)
                {
                    _logger.LogWarning("[Playback] MediaPlayer failed to open stream");
                    _mediaPlayer.Dispose();
                    _mediaPlayer = null;
                    _isStreaming = false;
                    return false;
                }
            }

            WireMediaPlayerEvents();

            if (audioBook.CurrentTime.TotalSeconds > 0)
                _mediaPlayer.PlaybackSession.Position = audioBook.CurrentTime;

            SetState(PlaybackState.Paused);
            _logger.Log($"[Playback] Stream loaded. Duration: {Duration}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[Playback] Stream exception: {ex.Message}");
            _mediaPlayer?.Dispose();
            _mediaPlayer = null;
            _isStreaming = false;
            return false;
        }
    }

    private async Task<bool> LoadTempDownloadAsync(string url, AudioBook audioBook, CancellationToken ct)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "AudioBookshelfApp"); // Legacy folder name for backward compatibility
        Directory.CreateDirectory(tempDir);
        var tempPath = Path.Combine(tempDir, $"abs_stream_{audioBook.Id}.tmp");

        _logger.Log($"[Playback] Temp download to: {tempPath}");

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (msg, cert, chain, errors) =>
            {
                if (errors == System.Net.Security.SslPolicyErrors.None) return true;
                if (_settingsService.Settings.AllowSelfSignedCertificates && !string.IsNullOrEmpty(_apiService.ServerUrl))
                {
                    var host = new Uri(_apiService.ServerUrl).Host;
                    return msg.RequestUri != null && string.Equals(msg.RequestUri.Host, host, StringComparison.OrdinalIgnoreCase);
                }
                return false;
            }
        };

        using var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };

        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        using var networkStream = await response.Content.ReadAsStreamAsync(ct);
        using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920);

        var buffer = new byte[81920];
        long downloaded = 0;
        int bytesRead;

        while ((bytesRead = await networkStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, bytesRead, ct);
            downloaded += bytesRead;

            if (downloaded % (1024 * 1024) < 81920 && totalBytes.HasValue && totalBytes.Value > 0)
            {
                var pct = (int)(downloaded * 100 / totalBytes.Value);
                _logger.LogDebug($"[Playback] Temp download: {pct}%");
            }
        }

        fileStream.Close();
        _logger.Log($"[Playback] Temp download complete: {downloaded / 1024}KB");

        return await Task.FromResult(true).ContinueWith(_ => LoadLocalFileAsync(tempPath, audioBook, ct), ct).Unwrap();
    }

    public Task PlayAsync()
    {
        if (_isStreaming && _mediaPlayer != null)
        {
            _mediaPlayer.Play();
            _positionTimer?.Start();
            SetState(PlaybackState.Playing);
        }
        else if (_outputDevice != null && (_state == PlaybackState.Paused || _state == PlaybackState.Stopped))
        {
            _outputDevice.Play();
            _positionTimer?.Start();
            SetState(PlaybackState.Playing);
        }

        // Start session sync timer (12s interval)
        _sessionStartTime = DateTime.UtcNow;
        StartSessionSyncTimer();
        UpdateSmtcPlaybackState(true);

        return Task.CompletedTask;
    }

    public Task PauseAsync()
    {
        StopSessionSyncTimer();

        if (_isStreaming && _mediaPlayer != null)
        {
            _mediaPlayer.Pause();
            _positionTimer?.Stop();
            SetState(PlaybackState.Paused);
        }
        else if (_outputDevice != null && _state == PlaybackState.Playing)
        {
            _outputDevice.Pause();
            _positionTimer?.Stop();
            SetState(PlaybackState.Paused);
        }

        UpdateSmtcPlaybackState(false);
        // Sync progress immediately on pause
        _ = SyncProgressNowAsync();
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        StopSessionSyncTimer();
        _positionTimer?.Stop();

        // Flush final progress before stopping
        if (_currentAudioBook != null)
        {
            await _syncService.FlushPlaybackProgressAsync(
                _currentAudioBook.Id,
                Position.TotalSeconds,
                Duration.TotalSeconds,
                isFinished: false);
        }

        if (_isStreaming && _mediaPlayer != null)
        {
            _mediaPlayer.Pause();
            _mediaPlayer.Source = null;
            _mediaPlayer.Dispose();
            _mediaPlayer = null;
            _isStreaming = false;
        }

        _outputDevice?.Stop();
        _outputDevice?.Dispose();
        _outputDevice = null;

        _audioFileReader?.Dispose();
        _audioFileReader = null;

        _smtcPlayer?.Dispose();
        _smtcPlayer = null;

        if (_currentSession != null)
            _ = CloseSessionAsync();

        SetState(PlaybackState.Stopped);
    }

    public async Task SeekAsync(TimeSpan position)
    {
        // For multi-track, check if we need to switch tracks
        if (_trackDurations.Count > 1)
        {
            int targetTrack = 0;
            for (int i = 0; i < _trackDurations.Count; i++)
            {
                if (position.TotalSeconds < _trackDurations[i])
                {
                    targetTrack = i;
                    break;
                }
                if (i == _trackDurations.Count - 1)
                    targetTrack = i;
            }

            if (targetTrack != _currentTrackIndex)
            {
                _currentTrackIndex = targetTrack;
                TrackChanged?.Invoke(this, _currentTrackIndex);
                var wasPlaying = _state == PlaybackState.Playing;

                if (_isStreaming && _streamTracks.Count > targetTrack && _currentAudioBook != null)
                {
                    await LoadStreamAsync(_streamTracks[targetTrack].ContentUrl, _currentAudioBook, CancellationToken.None);
                }
                else if (!_isStreaming && _trackPaths.Count > targetTrack)
                {
                    await LoadLocalTrackAsync(_trackPaths[targetTrack], CancellationToken.None);
                }

                var withinTrack = GetWithinTrackOffset(position);
                if (_isStreaming && _mediaPlayer != null)
                    _mediaPlayer.PlaybackSession.Position = withinTrack;
                else if (_audioFileReader != null)
                    _audioFileReader.CurrentTime = withinTrack;

                if (wasPlaying) await PlayAsync();
                PositionChanged?.Invoke(this, position);
                return;
            }
        }

        // Same track — simple seek
        if (_isStreaming && _mediaPlayer != null)
        {
            // For streaming, position is within current track
            var withinTrack = _trackDurations.Count > 1 ? GetWithinTrackOffset(position) : position;
            _mediaPlayer.PlaybackSession.Position = withinTrack;
        }
        else if (_audioFileReader != null)
        {
            var withinTrack = _trackDurations.Count > 1 ? GetWithinTrackOffset(position) : position;
            _audioFileReader.CurrentTime = withinTrack;
        }

        PositionChanged?.Invoke(this, position);
    }

    public Task SetPlaybackSpeedAsync(float speed)
    {
        _playbackSpeed = Math.Clamp(speed, 0.5f, 3.0f);
        if (_isStreaming && _mediaPlayer != null)
            _mediaPlayer.PlaybackSession.PlaybackRate = _playbackSpeed;
        return Task.CompletedTask;
    }

    public Task SetVolumeAsync(float volume)
    {
        _volume = Math.Clamp(volume, 0f, 1f);
        if (_isStreaming && _mediaPlayer != null)
            _mediaPlayer.Volume = _volume;
        else if (_outputDevice != null)
            _outputDevice.Volume = _volume;
        return Task.CompletedTask;
    }

    private void WireNAudioEvents()
    {
        if (_outputDevice == null) return;
        _outputDevice.PlaybackStopped += (s, e) =>
        {
            if (e.Exception != null)
            {
                StopSessionSyncTimer();
                _logger.LogError("[Playback] NAudio error", e.Exception);
                SetState(PlaybackState.Stopped, e.Exception.Message);
                return;
            }

            // Auto-advance to next track if available
            if (!_autoAdvancing && _currentTrackIndex + 1 < _trackPaths.Count)
            {
                _autoAdvancing = true;
                _currentTrackIndex++;
                TrackChanged?.Invoke(this, _currentTrackIndex);
                _logger.Log($"[Playback] Auto-advancing to track {_currentTrackIndex + 1}/{TotalTracks}");
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await LoadLocalTrackAsync(_trackPaths[_currentTrackIndex], CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("[Playback] Track advance failed", ex);
                        SetState(PlaybackState.Stopped, ex.Message);
                    }
                    finally { _autoAdvancing = false; }
                });
                return;
            }

            // No more tracks — end of book
            StopSessionSyncTimer();
            _logger.Log("[Playback] End of book (all tracks)");
            SetState(PlaybackState.Stopped);
            if (_currentAudioBook != null)
            {
                _ = _syncService.FlushPlaybackProgressAsync(
                    _currentAudioBook.Id,
                    Position.TotalSeconds,
                    Duration.TotalSeconds,
                    isFinished: true);
            }
            if (_currentSession != null)
                _ = CloseSessionAsync();
        };
    }

    private void WireMediaPlayerEvents()
    {
        if (_mediaPlayer == null) return;

        _mediaPlayer.MediaEnded += (s, a) =>
        {
            // Auto-advance to next stream track if available
            if (!_autoAdvancing && _currentTrackIndex + 1 < _streamTracks.Count)
            {
                _autoAdvancing = true;
                _currentTrackIndex++;
                TrackChanged?.Invoke(this, _currentTrackIndex);
                _logger.Log($"[Playback] Stream auto-advancing to track {_currentTrackIndex + 1}/{TotalTracks}");
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (_currentAudioBook != null)
                            await LoadStreamAsync(_streamTracks[_currentTrackIndex].ContentUrl, _currentAudioBook, CancellationToken.None);
                        await PlayAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("[Playback] Stream track advance failed", ex);
                        SetState(PlaybackState.Stopped, ex.Message);
                    }
                    finally { _autoAdvancing = false; }
                });
                return;
            }

            // No more tracks
            StopSessionSyncTimer();
            _logger.Log("[Playback] Stream ended (all tracks)");
            SetState(PlaybackState.Stopped);
            if (_currentAudioBook != null)
            {
                _ = _syncService.FlushPlaybackProgressAsync(
                    _currentAudioBook.Id,
                    Position.TotalSeconds,
                    Duration.TotalSeconds,
                    isFinished: true);
            }
            if (_currentSession != null)
                _ = CloseSessionAsync();
        };

        _mediaPlayer.MediaFailed += (s, a) =>
        {
            _logger.LogError($"[Playback] MediaPlayer error: {a.ErrorMessage}");
            SetState(PlaybackState.Stopped, a.ErrorMessage);
        };

        _mediaPlayer.BufferingStarted += (s, a) =>
        {
            if (_state == PlaybackState.Playing)
                SetState(PlaybackState.Buffering);
        };

        _mediaPlayer.BufferingEnded += (s, a) =>
        {
            if (_state == PlaybackState.Buffering)
                SetState(PlaybackState.Playing);
        };
    }

    private void SetState(PlaybackState newState, string? errorMessage = null)
    {
        var oldState = _state;
        _state = newState;
        _logger.LogDebug($"[Playback] {oldState} -> {newState}{(errorMessage != null ? $" ({errorMessage})" : "")}");
        PlaybackStateChanged?.Invoke(this, new PlaybackStateChangedEventArgs
        {
            State = newState,
            OldState = oldState,
            ErrorMessage = errorMessage
        });
    }

    private void StartSessionSyncTimer()
    {
        StopSessionSyncTimer();
        _sessionSyncCts = new CancellationTokenSource();
        _sessionSyncTimer = new PeriodicTimer(TimeSpan.FromSeconds(12));
        _ = RunSessionSyncLoopAsync(_sessionSyncCts.Token);
    }

    private void StopSessionSyncTimer()
    {
        _sessionSyncCts?.Cancel();
        _sessionSyncTimer?.Dispose();
        _sessionSyncTimer = null;
        _sessionSyncCts?.Dispose();
        _sessionSyncCts = null;
    }

    private async Task RunSessionSyncLoopAsync(CancellationToken ct)
    {
        try
        {
            while (_sessionSyncTimer != null && await _sessionSyncTimer.WaitForNextTickAsync(ct))
            {
                await SyncProgressNowAsync();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogDebug($"[Playback] Session sync loop error: {ex.Message}");
        }
    }

    private async Task SyncProgressNowAsync()
    {
        if (_currentAudioBook == null) return;

        var currentTime = Position.TotalSeconds;
        var duration = Duration.TotalSeconds;

        // Save to local DB first (crash safety)
        try
        {
            await _database.SavePlaybackProgressAsync(
                _currentAudioBook.Id,
                TimeSpan.FromSeconds(currentTime),
                isFinished: false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"[Playback] Local progress save failed: {ex.Message}");
        }

        // Sync to server via session if available
        if (_currentSession != null && _apiService.IsAuthenticated)
        {
            try
            {
                var elapsed = (DateTime.UtcNow - _sessionStartTime).TotalSeconds;
                _accumulatedListenTime += 12; // approximate listen time since last sync
                await _apiService.SyncSessionProgressAsync(
                    _currentSession.Id, currentTime, duration, _accumulatedListenTime);
                _logger.LogDebug($"[Playback] Session sync: {currentTime:F1}s, listened: {_accumulatedListenTime:F0}s");
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[Playback] Session sync failed: {ex.Message}");
            }
        }
        else
        {
            // Fallback: use SyncService throttled progress reporting
            await _syncService.ReportPlaybackPositionAsync(
                _currentAudioBook.Id, currentTime, duration);
        }
    }

    // Chapter helpers
    private void UpdateCurrentChapter()
    {
        if (_chapters.Count == 0) return;

        var posSeconds = Position.TotalSeconds;
        int newIndex = -1;
        for (int i = 0; i < _chapters.Count; i++)
        {
            if (posSeconds >= _chapters[i].Start && posSeconds < _chapters[i].End)
            {
                newIndex = i;
                break;
            }
        }

        if (newIndex != _currentChapterIndex)
        {
            _currentChapterIndex = newIndex;
            ChapterChanged?.Invoke(this, CurrentChapter);
        }
    }

    public async Task SeekToChapterAsync(int chapterIndex)
    {
        if (chapterIndex < 0 || chapterIndex >= _chapters.Count) return;
        var chapter = _chapters[chapterIndex];
        await SeekAsync(TimeSpan.FromSeconds(chapter.Start));
        _currentChapterIndex = chapterIndex;
        ChapterChanged?.Invoke(this, chapter);
    }

    // Multi-track helpers
    private void BuildLocalTrackList(AudioBook audioBook, string primaryPath)
    {
        _trackPaths.Clear();
        _trackDurations.Clear();

        if (audioBook.AudioFiles.Count <= 1)
        {
            // Single file
            _trackPaths.Add(primaryPath);
            return;
        }

        // Multi-file: find all local files in the same directory
        var dir = Path.GetDirectoryName(primaryPath);
        if (dir == null)
        {
            _trackPaths.Add(primaryPath);
            return;
        }

        double cumulative = 0;
        foreach (var af in audioBook.AudioFiles.OrderBy(f => f.Index))
        {
            var localPath = af.LocalPath;
            if (string.IsNullOrEmpty(localPath))
            {
                // Try to find file in the download directory
                var candidate = Path.Combine(dir, Path.GetFileName(af.Filename));
                if (File.Exists(candidate))
                    localPath = candidate;
            }

            if (!string.IsNullOrEmpty(localPath) && File.Exists(localPath))
            {
                _trackPaths.Add(localPath);
                cumulative += af.Duration.TotalSeconds;
                _trackDurations.Add(cumulative);
            }
        }

        // Fallback: if we couldn't build a multi-track list, use the primary file
        if (_trackPaths.Count == 0)
        {
            _trackPaths.Add(primaryPath);
            _trackDurations.Clear();
        }

        _logger.Log($"[Playback] Built track list: {_trackPaths.Count} local files");
    }

    private void BuildStreamTrackDurations()
    {
        _trackDurations.Clear();
        double cumulative = 0;
        foreach (var track in _streamTracks)
        {
            cumulative += track.Duration;
            _trackDurations.Add(cumulative);
        }
    }

    private void DetermineStartingTrack(TimeSpan currentTime)
    {
        if (_trackDurations.Count == 0 || currentTime.TotalSeconds <= 0)
        {
            _currentTrackIndex = 0;
            return;
        }

        double target = currentTime.TotalSeconds;
        for (int i = 0; i < _trackDurations.Count; i++)
        {
            if (target < _trackDurations[i])
            {
                _currentTrackIndex = i;
                return;
            }
        }
        _currentTrackIndex = Math.Max(0, _trackDurations.Count - 1);
    }

    private TimeSpan GetWithinTrackOffset(TimeSpan overallPosition)
    {
        if (_currentTrackIndex == 0 || _trackDurations.Count == 0)
            return overallPosition;

        var previousCumulative = _currentTrackIndex > 0 ? _trackDurations[_currentTrackIndex - 1] : 0;
        var offset = overallPosition.TotalSeconds - previousCumulative;
        return TimeSpan.FromSeconds(Math.Max(0, offset));
    }

    private async Task CloseSessionAsync()
    {
        if (_currentSession == null) return;
        try
        {
            await _apiService.ClosePlaybackSessionAsync(_currentSession.Id);
            _logger.Log($"[Playback] Session closed");
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"[Playback] Session close failed: {ex.Message}");
        }
        _currentSession = null;
    }

    // === SMTC (System Media Transport Controls) ===

    private void SetupSmtc(AudioBook book)
    {
        try
        {
            if (_isStreaming && _mediaPlayer != null)
            {
                // Streaming MediaPlayer has built-in SMTC — configure display + controls
                ConfigureSmtcForMediaPlayer(_mediaPlayer, book);
                _logger.Log("[SMTC] Configured on streaming MediaPlayer");
                return;
            }

            // NAudio path: create a background MediaPlayer solely for SMTC
            _smtcPlayer?.Dispose();
            _smtcPlayer = new Windows.Media.Playback.MediaPlayer
            {
                AutoPlay = false,
                Volume = 0 // silent — only used for SMTC registration
            };

            ConfigureSmtcForMediaPlayer(_smtcPlayer, book);
            _smtcPlayer.CommandManager.PlayReceived += (s, a) => _ = PlayAsync();
            _smtcPlayer.CommandManager.PauseReceived += (s, a) => _ = PauseAsync();
            _smtcPlayer.CommandManager.NextReceived += (s, a) =>
            {
                var pos = Position + TimeSpan.FromSeconds(30);
                if (pos > Duration) pos = Duration;
                _ = SeekAsync(pos);
            };
            _smtcPlayer.CommandManager.PreviousReceived += (s, a) =>
            {
                var pos = Position - TimeSpan.FromSeconds(10);
                if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
                _ = SeekAsync(pos);
            };

            _logger.Log("[SMTC] Configured via background MediaPlayer for NAudio");
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"[SMTC] Setup failed (non-fatal): {ex.Message}");
        }
    }

    private void ConfigureSmtcForMediaPlayer(Windows.Media.Playback.MediaPlayer player, AudioBook book)
    {
        player.CommandManager.IsEnabled = true;
        player.SystemMediaTransportControls.IsEnabled = true;
        player.SystemMediaTransportControls.IsPlayEnabled = true;
        player.SystemMediaTransportControls.IsPauseEnabled = true;
        player.SystemMediaTransportControls.IsNextEnabled = true;
        player.SystemMediaTransportControls.IsPreviousEnabled = true;

        var updater = player.SystemMediaTransportControls.DisplayUpdater;
        updater.Type = Windows.Media.MediaPlaybackType.Music;
        updater.MusicProperties.Title = book.Title ?? "Unknown";
        updater.MusicProperties.Artist = book.Author ?? "Unknown";
        updater.Update();
    }

    private void UpdateSmtcPlaybackState(bool isPlaying)
    {
        try
        {
            var smtc = _smtcPlayer?.SystemMediaTransportControls
                       ?? (_isStreaming ? _mediaPlayer?.SystemMediaTransportControls : null);
            if (smtc != null)
            {
                smtc.PlaybackStatus = isPlaying
                    ? Windows.Media.MediaPlaybackStatus.Playing
                    : Windows.Media.MediaPlaybackStatus.Paused;
            }
        }
        catch { /* non-fatal */ }
    }

    public void Dispose()
    {
        StopSessionSyncTimer();
        _positionTimer?.Dispose();
        _outputDevice?.Dispose();
        _audioFileReader?.Dispose();
        _mediaPlayer?.Dispose();
        _smtcPlayer?.Dispose();
        _loadCts?.Dispose();
    }
}
