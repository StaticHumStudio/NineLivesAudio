using NineLivesAudio.Models;
using NineLivesAudio.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NineLivesAudio.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IAudioPlaybackService _playbackService;
    private readonly IAudioBookshelfApiService _apiService;
    private readonly ISettingsService _settingsService;
    private readonly ISyncService _syncService;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _isMiniPlayerVisible;

    [ObservableProperty]
    private AudioBook? _currentAudioBook;

    [ObservableProperty]
    private TimeSpan _currentPosition;

    [ObservableProperty]
    private TimeSpan _duration;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _isAuthenticated;

    [ObservableProperty]
    private bool _isSyncing;

    public MainViewModel(
        IAudioPlaybackService playbackService,
        IAudioBookshelfApiService apiService,
        ISettingsService settingsService,
        ISyncService syncService)
    {
        _playbackService = playbackService;
        _apiService = apiService;
        _settingsService = settingsService;
        _syncService = syncService;

        // Subscribe to playback events
        _playbackService.PlaybackStateChanged += OnPlaybackStateChanged;
        _playbackService.PositionChanged += OnPositionChanged;
        _syncService.SyncStarted += OnSyncStarted;
        _syncService.SyncCompleted += OnSyncCompleted;
        _syncService.SyncFailed += OnSyncFailed;

        IsAuthenticated = _apiService.IsAuthenticated;
    }

    private void OnPlaybackStateChanged(object? sender, PlaybackStateChangedEventArgs e)
    {
        IsPlaying = e.State == PlaybackState.Playing;
        CurrentAudioBook = _playbackService.CurrentAudioBook;
        IsMiniPlayerVisible = CurrentAudioBook != null;
        Duration = _playbackService.Duration;
    }

    private void OnPositionChanged(object? sender, TimeSpan position)
    {
        CurrentPosition = position;
        if (Duration.TotalSeconds > 0)
        {
            Progress = position.TotalSeconds / Duration.TotalSeconds * 100;
        }
    }

    private void OnSyncStarted(object? sender, SyncEventArgs e) => IsSyncing = true;
    private void OnSyncCompleted(object? sender, SyncEventArgs e) => IsSyncing = false;
    private void OnSyncFailed(object? sender, SyncErrorEventArgs e) => IsSyncing = false;

    public void Dispose()
    {
        _playbackService.PlaybackStateChanged -= OnPlaybackStateChanged;
        _playbackService.PositionChanged -= OnPositionChanged;
        _syncService.SyncStarted -= OnSyncStarted;
        _syncService.SyncCompleted -= OnSyncCompleted;
        _syncService.SyncFailed -= OnSyncFailed;
    }

    [RelayCommand]
    private async Task PlayPauseAsync()
    {
        if (IsPlaying)
        {
            await _playbackService.PauseAsync();
        }
        else
        {
            await _playbackService.PlayAsync();
        }
    }

    [RelayCommand]
    private async Task SkipForwardAsync()
    {
        var newPosition = CurrentPosition + TimeSpan.FromSeconds(30);
        if (newPosition > Duration)
            newPosition = Duration;
        await _playbackService.SeekAsync(newPosition);
    }

    [RelayCommand]
    private async Task SkipBackwardAsync()
    {
        var newPosition = CurrentPosition - TimeSpan.FromSeconds(10);
        if (newPosition < TimeSpan.Zero)
            newPosition = TimeSpan.Zero;
        await _playbackService.SeekAsync(newPosition);
    }

    [RelayCommand]
    private async Task SyncAsync()
    {
        await _syncService.SyncNowAsync();
    }
}
