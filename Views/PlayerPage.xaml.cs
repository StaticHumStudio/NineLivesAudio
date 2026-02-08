using NineLivesAudio.Models;
using NineLivesAudio.Services;
using NineLivesAudio.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace NineLivesAudio.Views;

public sealed partial class PlayerPage : Page
{
    public PlayerViewModel ViewModel { get; }
    private readonly ILoggingService _logger;
    private readonly IMetadataNormalizer _normalizer;
    private readonly IAudioPlaybackService _playbackService;
    private readonly IAudioBookshelfApiService _apiService;
    private bool _isUserSeeking = false;
    private bool _isChapterMode = false;

    // Cover art cache to prevent flashing on frequent updates
    private string? _lastCoverPath;
    private BitmapImage? _cachedCoverImage;

    public PlayerPage()
    {
        ViewModel = App.Services.GetRequiredService<PlayerViewModel>();
        _logger = App.Services.GetRequiredService<ILoggingService>();
        _normalizer = App.Services.GetRequiredService<IMetadataNormalizer>();
        _playbackService = App.Services.GetRequiredService<IAudioPlaybackService>();
        _apiService = App.Services.GetRequiredService<IAudioBookshelfApiService>();
        this.InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _logger.Log("PlayerPage loaded");
        ViewModel.Initialize();

        // Subscribe to ViewModel property changes
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        // Subscribe to playback events for real-time updates
        _playbackService.PositionChanged += PlaybackService_PositionChanged;
        _playbackService.PlaybackStateChanged += PlaybackService_StateChanged;
        _playbackService.TrackChanged += PlaybackService_TrackChanged;
        _playbackService.ChapterChanged += PlaybackService_ChapterChanged;

        // Initial UI update
        UpdateAllUI();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _logger.Log("PlayerPage unloaded, cleaning up event subscriptions");

        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _playbackService.PositionChanged -= PlaybackService_PositionChanged;
        _playbackService.PlaybackStateChanged -= PlaybackService_StateChanged;
        _playbackService.TrackChanged -= PlaybackService_TrackChanged;
        _playbackService.ChapterChanged -= PlaybackService_ChapterChanged;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(PlayerViewModel.CurrentAudioBook):
                    UpdateBookUI();
                    break;
                case nameof(PlayerViewModel.IsPlaying):
                case nameof(PlayerViewModel.IsLoading):
                case nameof(PlayerViewModel.PlaybackSpeed):
                case nameof(PlayerViewModel.Volume):
                case nameof(PlayerViewModel.SleepTimerMinutes):
                case nameof(PlayerViewModel.SleepTimerText):
                    UpdateControlsUI();
                    break;
                // Position/time changes are handled by PositionChanged event — ignore here
            }
        });
    }

    private void PlaybackService_PositionChanged(object? sender, TimeSpan position)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_isUserSeeking)
            {
                if (_isChapterMode)
                {
                    var chapter = _playbackService.CurrentChapter;
                    if (chapter != null && chapter.Duration.TotalSeconds > 0)
                    {
                        var chapterElapsed = position.TotalSeconds - chapter.Start;
                        if (chapterElapsed < 0) chapterElapsed = 0;
                        ProgressSlider.Value = chapterElapsed / chapter.Duration.TotalSeconds * 100;

                        CurrentTimeText.Text = FormatTimeSpan(TimeSpan.FromSeconds(chapterElapsed));
                        var chapterRemaining = chapter.Duration - TimeSpan.FromSeconds(chapterElapsed);
                        if (chapterRemaining < TimeSpan.Zero) chapterRemaining = TimeSpan.Zero;
                        RemainingTimeText.Text = $"-{FormatTimeSpan(chapterRemaining)}";
                    }
                }
                else
                {
                    var duration = ViewModel.Duration;
                    if (duration.TotalSeconds > 0)
                    {
                        ProgressSlider.Value = position.TotalSeconds / duration.TotalSeconds * 100;
                    }

                    CurrentTimeText.Text = FormatTimeSpan(position);
                    var remaining = duration - position;
                    if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
                    RemainingTimeText.Text = $"-{FormatTimeSpan(remaining)}";
                }
            }
        });
    }

    private void PlaybackService_StateChanged(object? sender, PlaybackStateChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _logger.LogDebug($"PlayerPage received state change: {e.State}");
            UpdateAllUI();
        });
    }

    private void PlaybackService_TrackChanged(object? sender, int trackIndex)
    {
        DispatcherQueue.TryEnqueue(() => UpdateTrackIndicator());
    }

    private void UpdateAllUI()
    {
        UpdateControlsUI();
        UpdateBookUI();
    }

    private void UpdateControlsUI()
    {
        var audioBook = ViewModel.CurrentAudioBook;
        var isPlaying = ViewModel.IsPlaying;
        var isLoading = ViewModel.IsLoading;

        // Visibility states
        bool hasBook = audioBook != null;
        EmptyStatePanel.Visibility = (!hasBook && !isLoading) ? Visibility.Visible : Visibility.Collapsed;
        PlayerContent.Visibility = hasBook ? Visibility.Visible : Visibility.Collapsed;
        LoadingOverlay.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;

        // Play/Pause icon
        PlayPauseIcon.Glyph = isPlaying ? "\uE769" : "\uE768";

        // Speed text
        SpeedText.Text = $"{ViewModel.PlaybackSpeed:0.0}x";

        // Sleep timer
        SleepButtonText.Text = ViewModel.SleepTimerMinutes.HasValue ? $"{ViewModel.SleepTimerMinutes}m" : "Off";
        SleepTimerPanel.Visibility = ViewModel.SleepTimerMinutes.HasValue ? Visibility.Visible : Visibility.Collapsed;
        SleepTimerStatusText.Text = ViewModel.SleepTimerText;

        // Volume
        VolumeSlider.Value = ViewModel.Volume;
        UpdateVolumeIcon();

        // Time texts
        CurrentTimeText.Text = ViewModel.CurrentPositionText;
        RemainingTimeText.Text = ViewModel.RemainingTimeText;
    }

    private void UpdateBookUI()
    {
        var audioBook = ViewModel.CurrentAudioBook;
        if (audioBook == null) return;

        // Use normalizer for clean display
        var normalized = _normalizer.Normalize(audioBook);
        TitleText.Text = normalized.DisplayTitle;
        AuthorText.Text = normalized.DisplayAuthor;

        // Series info
        if (!string.IsNullOrEmpty(normalized.DisplaySeries))
        {
            SeriesText.Text = normalized.DisplaySeries;
            SeriesText.Visibility = Visibility.Visible;
        }
        else
        {
            SeriesText.Visibility = Visibility.Collapsed;
        }

        // Source badge (local vs streaming)
        UpdateSourceBadge();

        // Track indicator (multi-file)
        UpdateTrackIndicator();

        // Chapter UI
        UpdateChapterUI();

        // Cover image — only reassign if the cover path actually changed
        var coverPath = audioBook.CoverPath;
        if (coverPath != _lastCoverPath)
        {
            _lastCoverPath = coverPath;
            if (!string.IsNullOrEmpty(coverPath))
            {
                try
                {
                    _cachedCoverImage = new BitmapImage(new Uri(coverPath));
                    CoverImage.Source = _cachedCoverImage;
                }
                catch
                {
                    _cachedCoverImage = null;
                    CoverImage.Source = null;
                }
            }
            else
            {
                _cachedCoverImage = null;
                CoverImage.Source = null;
            }
        }
    }

    private void UpdateSourceBadge()
    {
        var isLocal = _playbackService.IsPlayingLocalFile;
        var state = _playbackService.State;

        // Only show badge when actively loaded/playing
        if (state == PlaybackState.Stopped)
        {
            SourceBadge.Visibility = Visibility.Collapsed;
            return;
        }

        SourceBadge.Visibility = Visibility.Visible;

        if (isLocal)
        {
            SourceBadge.Background = (Brush)Application.Current.Resources["NebulaDarkBrush"];
            SourceBadgeIcon.Glyph = "\uE73E"; // Checkmark
            SourceBadgeText.Text = "Playing from local file";
        }
        else
        {
            SourceBadge.Background = (Brush)Application.Current.Resources["VoidElevatedBrush"];
            SourceBadgeIcon.Glyph = "\uE753"; // Cloud/wifi
            SourceBadgeText.Text = "Streaming from server";
        }
    }

    private void UpdateTrackIndicator()
    {
        var totalTracks = _playbackService.TotalTracks;
        if (totalTracks > 1)
        {
            TrackIndicator.Text = $"Track {_playbackService.CurrentTrackIndex + 1} of {totalTracks}";
            TrackIndicator.Visibility = Visibility.Visible;
        }
        else
        {
            TrackIndicator.Visibility = Visibility.Collapsed;
        }
    }

    private void PlaybackService_ChapterChanged(object? sender, Chapter? chapter)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateChapterUI();
            if (_isChapterMode)
            {
                RefreshProgressDisplay();
            }
        });
    }

    private void UpdateChapterUI()
    {
        var chapters = _playbackService.Chapters;
        var hasChapters = chapters.Count > 0;

        ChapterNameText.Visibility = hasChapters ? Visibility.Visible : Visibility.Collapsed;
        PrevChapterButton.Visibility = hasChapters ? Visibility.Visible : Visibility.Collapsed;
        NextChapterButton.Visibility = hasChapters ? Visibility.Visible : Visibility.Collapsed;
        ChaptersButtonPanel.Visibility = hasChapters ? Visibility.Visible : Visibility.Collapsed;
        ChaptersColumn.Width = hasChapters ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

        // Show/hide chapter mode toggle
        ChapterModePanel.Visibility = hasChapters ? Visibility.Visible : Visibility.Collapsed;

        // If no chapters and chapter mode was on, turn it off
        if (!hasChapters && _isChapterMode)
        {
            _isChapterMode = false;
            ChapterModeToggle.IsOn = false;
        }

        var current = _playbackService.CurrentChapter;
        if (current != null)
        {
            ChapterNameText.Text = current.Title;
        }
        else if (hasChapters)
        {
            ChapterNameText.Text = $"{chapters.Count} chapters";
        }
    }

    private async void PrevChapter_Click(object sender, RoutedEventArgs e)
    {
        var idx = _playbackService.CurrentChapterIndex;
        if (idx > 0)
            await _playbackService.SeekToChapterAsync(idx - 1);
        else if (idx == 0)
            await _playbackService.SeekToChapterAsync(0); // restart current chapter
    }

    private async void NextChapter_Click(object sender, RoutedEventArgs e)
    {
        var idx = _playbackService.CurrentChapterIndex;
        var chapters = _playbackService.Chapters;
        if (idx + 1 < chapters.Count)
            await _playbackService.SeekToChapterAsync(idx + 1);
    }

    private void ChaptersButton_Click(object sender, RoutedEventArgs e)
    {
        var flyout = new MenuFlyout();
        var chapters = _playbackService.Chapters;
        var currentIdx = _playbackService.CurrentChapterIndex;

        for (int i = 0; i < chapters.Count; i++)
        {
            var chapter = chapters[i];
            var item = new MenuFlyoutItem
            {
                Text = $"{i + 1}. {chapter.Title}",
                Icon = i == currentIdx ? new FontIcon { Glyph = "\uE768" } : null
            };

            var capturedIndex = i;
            item.Click += async (s, args) =>
            {
                await _playbackService.SeekToChapterAsync(capturedIndex);
            };
            flyout.Items.Add(item);
        }

        flyout.ShowAt(ChaptersButton);
    }

    private void UpdateVolumeIcon()
    {
        var vol = ViewModel.Volume;
        if (vol <= 0) VolumeIcon.Glyph = "\uE74F";
        else if (vol < 0.3) VolumeIcon.Glyph = "\uE993";
        else if (vol < 0.7) VolumeIcon.Glyph = "\uE994";
        else VolumeIcon.Glyph = "\uE995";
    }

    private static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
    }

    private void ProgressSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        // Only seek when user is interacting with the slider
        if (sender is Slider slider && slider.FocusState != FocusState.Unfocused)
        {
            _isUserSeeking = true;

            if (_isChapterMode)
            {
                var chapter = _playbackService.CurrentChapter;
                if (chapter != null && chapter.Duration.TotalSeconds > 0)
                {
                    // Map slider percentage to absolute position within current chapter
                    var chapterPosition = chapter.Start + (chapter.Duration.TotalSeconds * e.NewValue / 100);
                    chapterPosition = Math.Clamp(chapterPosition, chapter.Start, chapter.End);
                    _ = _playbackService.SeekAsync(TimeSpan.FromSeconds(chapterPosition));
                }
            }
            else
            {
                _ = ViewModel.SeekCommand.ExecuteAsync(e.NewValue);
            }

            _isUserSeeking = false;
        }
    }

    private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (sender is Slider)
        {
            _ = ViewModel.SetVolumeCommand.ExecuteAsync((float)e.NewValue);
        }
    }

    private async void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.PlayPauseCommand.ExecuteAsync(null);
    }

    private async void SkipBack_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.SkipBackwardCommand.ExecuteAsync(null);
    }

    private async void SkipForward_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.SkipForwardCommand.ExecuteAsync(null);
    }

    private void CancelSleepTimer_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CancelSleepTimerCommand.Execute(null);
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
            Frame.GoBack();
    }

    private void GoToLibrary_Click(object sender, RoutedEventArgs e)
    {
        // Navigate to library page
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
        else
        {
            Frame.Navigate(typeof(LibraryPage));
        }
    }

    private void SpeedButton_Click(object sender, RoutedEventArgs e)
    {
        var flyout = new MenuFlyout();

        var speeds = new[] { 0.5f, 0.75f, 1.0f, 1.25f, 1.5f, 1.75f, 2.0f, 2.5f, 3.0f };

        foreach (var speed in speeds)
        {
            var item = new MenuFlyoutItem
            {
                Text = $"{speed:0.0}x",
                Icon = speed == ViewModel.PlaybackSpeed
                    ? new FontIcon { Glyph = "\uE73E" }
                    : null
            };

            var capturedSpeed = speed;
            item.Click += (s, args) =>
            {
                _ = ViewModel.SetPlaybackSpeedCommand.ExecuteAsync(capturedSpeed);
                SpeedText.Text = $"{capturedSpeed:0.0}x";
            };
            flyout.Items.Add(item);
        }

        flyout.ShowAt(SpeedButton);
    }

    private void SleepButton_Click(object sender, RoutedEventArgs e)
    {
        var flyout = new MenuFlyout();

        var options = new (int? minutes, string label)[]
        {
            (null, "Off"),
            (5, "5 minutes"),
            (10, "10 minutes"),
            (15, "15 minutes"),
            (30, "30 minutes"),
            (45, "45 minutes"),
            (60, "1 hour"),
            (90, "1.5 hours"),
            (120, "2 hours")
        };

        foreach (var (minutes, label) in options)
        {
            var item = new MenuFlyoutItem
            {
                Text = label,
                Icon = minutes == ViewModel.SleepTimerMinutes
                    ? new FontIcon { Glyph = "\uE73E" }
                    : null
            };

            var capturedMinutes = minutes;
            item.Click += (s, args) =>
            {
                ViewModel.SetSleepTimerCommand.Execute(capturedMinutes);
                UpdateAllUI();
            };
            flyout.Items.Add(item);
        }

        flyout.ShowAt(SleepButton);
    }

    private void ChapterModeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        _isChapterMode = ChapterModeToggle.IsOn;
        RefreshProgressDisplay();
    }

    private void RefreshProgressDisplay()
    {
        var position = _playbackService.Position;
        var duration = ViewModel.Duration;

        if (_isChapterMode)
        {
            var chapter = _playbackService.CurrentChapter;
            if (chapter != null && chapter.Duration.TotalSeconds > 0)
            {
                var chapterElapsed = position.TotalSeconds - chapter.Start;
                if (chapterElapsed < 0) chapterElapsed = 0;
                ProgressSlider.Value = chapterElapsed / chapter.Duration.TotalSeconds * 100;

                CurrentTimeText.Text = FormatTimeSpan(TimeSpan.FromSeconds(chapterElapsed));
                var chapterRemaining = chapter.Duration - TimeSpan.FromSeconds(chapterElapsed);
                if (chapterRemaining < TimeSpan.Zero) chapterRemaining = TimeSpan.Zero;
                RemainingTimeText.Text = $"-{FormatTimeSpan(chapterRemaining)}";
            }
        }
        else
        {
            if (duration.TotalSeconds > 0)
            {
                ProgressSlider.Value = position.TotalSeconds / duration.TotalSeconds * 100;
            }

            CurrentTimeText.Text = FormatTimeSpan(position);
            var remaining = duration - position;
            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
            RemainingTimeText.Text = $"-{FormatTimeSpan(remaining)}";
        }
    }

    private async void BookmarkButton_Click(object sender, RoutedEventArgs e)
    {
        var audioBook = ViewModel.CurrentAudioBook;
        if (audioBook == null) return;

        var flyout = new MenuFlyout();

        // "Add bookmark here" item
        var addItem = new MenuFlyoutItem
        {
            Text = "Add bookmark here",
            Icon = new FontIcon { Glyph = "\uE710" }
        };
        addItem.Click += async (s, args) =>
        {
            await CreateBookmarkAtCurrentPositionAsync();
        };
        flyout.Items.Add(addItem);

        flyout.Items.Add(new MenuFlyoutSeparator());

        // Load existing bookmarks
        try
        {
            var bookmarks = await _apiService.GetBookmarksAsync(audioBook.Id);

            if (bookmarks.Count == 0)
            {
                var emptyItem = new MenuFlyoutItem
                {
                    Text = "No bookmarks yet",
                    IsEnabled = false
                };
                flyout.Items.Add(emptyItem);
            }
            else
            {
                foreach (var bookmark in bookmarks)
                {
                    var subItem = new MenuFlyoutSubItem
                    {
                        Text = $"{bookmark.Title}  ({bookmark.TimeFormatted})",
                        Icon = new FontIcon { Glyph = "\uE8A4" }
                    };

                    var capturedTime = bookmark.Time;
                    var capturedItemId = audioBook.Id;

                    var goToItem = new MenuFlyoutItem
                    {
                        Text = "Go to position",
                        Icon = new FontIcon { Glyph = "\uE768" }
                    };
                    goToItem.Click += async (s, args) =>
                    {
                        await _playbackService.SeekAsync(TimeSpan.FromSeconds(capturedTime));
                    };
                    subItem.Items.Add(goToItem);

                    var deleteItem = new MenuFlyoutItem
                    {
                        Text = "Delete",
                        Icon = new FontIcon { Glyph = "\uE74D" }
                    };
                    deleteItem.Click += async (s, args) =>
                    {
                        var success = await _apiService.DeleteBookmarkAsync(capturedItemId, capturedTime);
                        if (success)
                            _logger.Log($"[Bookmarks] Deleted bookmark at {capturedTime:F1}s");
                    };
                    subItem.Items.Add(deleteItem);

                    flyout.Items.Add(subItem);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"Failed to load bookmarks: {ex.Message}");
            var errorItem = new MenuFlyoutItem
            {
                Text = "Failed to load bookmarks",
                IsEnabled = false
            };
            flyout.Items.Add(errorItem);
        }

        flyout.ShowAt(BookmarkButton);
    }

    private async Task CreateBookmarkAtCurrentPositionAsync()
    {
        var audioBook = ViewModel.CurrentAudioBook;
        if (audioBook == null) return;

        var currentTime = _playbackService.Position.TotalSeconds;

        // Generate a default title based on chapter info
        string defaultTitle;
        var chapter = _playbackService.CurrentChapter;
        var chapterIndex = _playbackService.CurrentChapterIndex;
        if (chapter != null && chapterIndex >= 0)
        {
            var chapterElapsed = TimeSpan.FromSeconds(currentTime - chapter.Start);
            defaultTitle = $"{chapter.Title} - {FormatTimeSpan(chapterElapsed)}";
        }
        else
        {
            defaultTitle = $"Bookmark at {FormatTimeSpan(_playbackService.Position)}";
        }

        // Show a ContentDialog to let user edit the bookmark title
        var dialog = new ContentDialog
        {
            Title = "Add Bookmark",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        var titleInput = new TextBox
        {
            Text = defaultTitle,
            PlaceholderText = "Bookmark title",
            SelectionStart = 0,
            SelectionLength = defaultTitle.Length
        };
        dialog.Content = titleInput;

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var title = string.IsNullOrWhiteSpace(titleInput.Text) ? defaultTitle : titleInput.Text;
            var success = await _apiService.CreateBookmarkAsync(audioBook.Id, title, currentTime);
            if (success)
            {
                _logger.Log($"[Bookmarks] Created: '{title}' at {currentTime:F1}s");
            }
            else
            {
                _logger.LogWarning($"[Bookmarks] Failed to create bookmark");
            }
        }
    }
}
