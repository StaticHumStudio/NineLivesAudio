using AudioBookshelfApp.Models;
using AudioBookshelfApp.Services;
using AudioBookshelfApp.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;

namespace AudioBookshelfApp.Views;

public sealed partial class BookDetailPage : Page
{
    private AudioBook? _audioBook;
    private bool _showChapterProgress;
    private bool _isUpdatingToggle;
    private readonly LibraryViewModel _libraryViewModel;
    private readonly IAudioPlaybackService _playbackService;
    private readonly IDownloadService _downloadService;
    private readonly ILoggingService _logger;

    public BookDetailPage()
    {
        this.InitializeComponent();
        _libraryViewModel = App.Services.GetRequiredService<LibraryViewModel>();
        _playbackService = App.Services.GetRequiredService<IAudioPlaybackService>();
        _downloadService = App.Services.GetRequiredService<IDownloadService>();
        _logger = App.Services.GetRequiredService<ILoggingService>();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is AudioBook audioBook)
        {
            _audioBook = audioBook;
            LoadBookDetails();
        }
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
    }

    private void LoadBookDetails()
    {
        if (_audioBook == null) return;

        _logger.Log($"Loading book details: {_audioBook.Title}");

        // Title — populate both wide and narrow
        TitleText.Text = _audioBook.Title;
        TitleTextNarrow.Text = _audioBook.Title;

        // Author (clickable)
        AuthorText.Text = $"by {_audioBook.Author}";
        AuthorTextNarrow.Text = $"by {_audioBook.Author}";

        // Narrator
        if (!string.IsNullOrEmpty(_audioBook.Narrator))
        {
            var narratorDisplay = $"Narrated by {_audioBook.Narrator}";
            NarratorText.Text = narratorDisplay;
            NarratorText.Visibility = Visibility.Visible;
            NarratorTextNarrow.Text = narratorDisplay;
            NarratorTextNarrow.Visibility = Visibility.Visible;
        }

        // Series (clickable link)
        if (!string.IsNullOrEmpty(_audioBook.SeriesName))
        {
            var seriesDisplay = _audioBook.SeriesName;
            if (!string.IsNullOrEmpty(_audioBook.SeriesSequence))
                seriesDisplay += $" #{_audioBook.SeriesSequence}";
            SeriesText.Text = seriesDisplay;
            SeriesLink.Visibility = Visibility.Visible;
            SeriesTextNarrow.Text = seriesDisplay;
            SeriesLinkNarrow.Visibility = Visibility.Visible;
        }

        // Duration
        var durationDisplay = FormatDuration(_audioBook.Duration);
        DurationText.Text = durationDisplay;
        DurationTextNarrow.Text = durationDisplay;

        // Added date
        if (_audioBook.AddedAt.HasValue)
        {
            var addedDisplay = $"Added {_audioBook.AddedAt.Value:MMMM d, yyyy}";
            AddedAtText.Text = addedDisplay;
            AddedAtText.Visibility = Visibility.Visible;
            AddedAtTextNarrow.Text = addedDisplay;
            AddedAtTextNarrow.Visibility = Visibility.Visible;
        }

        // Cover image — set both wide and narrow images
        if (!string.IsNullOrEmpty(_audioBook.CoverPath))
        {
            try
            {
                var coverBitmap = new BitmapImage(new Uri(_audioBook.CoverPath));
                CoverImage.Source = coverBitmap;
                CoverImageNarrow.Source = coverBitmap;
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"Failed to load cover: {ex.Message}");
            }
        }

        // Show chapter toggle if chapters exist
        var hasChapters = _audioBook.Chapters.Count > 0;
        ChapterToggle.Visibility = hasChapters ? Visibility.Visible : Visibility.Collapsed;
        ChapterToggleNarrow.Visibility = hasChapters ? Visibility.Visible : Visibility.Collapsed;

        // Progress display
        UpdateProgressDisplay();

        // Metadata details panel
        BuildMetadataPanel();

        // Genres
        if (_audioBook.Genres.Count > 0)
        {
            GenresRepeater.ItemsSource = _audioBook.Genres;
            GenresPanel.Visibility = Visibility.Visible;
        }

        // Tags
        if (_audioBook.Tags.Count > 0)
        {
            TagsRepeater.ItemsSource = _audioBook.Tags;
            TagsPanel.Visibility = Visibility.Visible;
        }

        // Description
        if (!string.IsNullOrEmpty(_audioBook.Description))
        {
            DescriptionText.Text = _audioBook.Description;
            DescriptionPanel.Visibility = Visibility.Visible;
        }

        // Download state
        UpdateDownloadState();

        // Chapters
        LoadChapters();

        // Tracks
        LoadTracks();
    }

    private void ChapterToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingToggle) return;
        _isUpdatingToggle = true;
        try
        {
            // Sync both toggles
            var isOn = (sender as ToggleSwitch)?.IsOn ?? false;
            _showChapterProgress = isOn;
            if (ChapterToggle.IsOn != isOn) ChapterToggle.IsOn = isOn;
            if (ChapterToggleNarrow.IsOn != isOn) ChapterToggleNarrow.IsOn = isOn;
            UpdateProgressDisplay();
        }
        finally
        {
            _isUpdatingToggle = false;
        }
    }

    private void UpdateProgressDisplay()
    {
        if (_audioBook == null) return;

        var progressPercentValue = _audioBook.ProgressPercent;

        if (_showChapterProgress && _audioBook.Chapters.Count > 0 && progressPercentValue > 0)
        {
            // Chapter progress mode
            var chIdx = _audioBook.GetCurrentChapterIndex();
            var chapter = _audioBook.GetCurrentChapter();
            var chPct = _audioBook.CurrentChapterProgressPercent;

            if (chapter != null && chIdx >= 0)
            {
                var chapterDisplay = $"Chapter {chIdx + 1}/{_audioBook.Chapters.Count}: {chapter.Title}";
                ProgressText.Text = chapterDisplay;
                ProgressTextNarrow.Text = chapterDisplay;
                ProgressBar.Value = chPct;
                ProgressBarNarrow.Value = chPct;

                var elapsed = FormatDuration(TimeSpan.FromSeconds(_audioBook.CurrentTime.TotalSeconds - chapter.Start));
                var chDur = FormatDuration(chapter.Duration);
                var chInfo = $"{(int)chPct}% of chapter ({elapsed} / {chDur})";
                ChapterInfoText.Text = chInfo;
                ChapterInfoTextNarrow.Text = chInfo;
                ChapterInfoText.Visibility = Visibility.Visible;
                ChapterInfoTextNarrow.Visibility = Visibility.Visible;
            }
            else
            {
                // Fallback to book progress
                ShowBookProgress(progressPercentValue);
            }

            PlayButtonText.Text = "Continue";
        }
        else if (progressPercentValue > 0)
        {
            // Book progress mode
            ShowBookProgress(progressPercentValue);
            PlayButtonText.Text = "Continue";
        }
        else
        {
            ProgressText.Text = "Not started";
            ProgressTextNarrow.Text = "Not started";
            ProgressBar.Value = 0;
            ProgressBarNarrow.Value = 0;
            ChapterInfoText.Visibility = Visibility.Collapsed;
            ChapterInfoTextNarrow.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowBookProgress(double progressPercentValue)
    {
        ProgressBar.Value = progressPercentValue;
        ProgressBarNarrow.Value = progressPercentValue;
        var currentPosition = FormatDuration(_audioBook!.CurrentTime);
        var totalDuration = FormatDuration(_audioBook.Duration);
        var progressDisplay = $"{(int)progressPercentValue}% complete ({currentPosition} / {totalDuration})";
        ProgressText.Text = progressDisplay;
        ProgressTextNarrow.Text = progressDisplay;
        ChapterInfoText.Visibility = Visibility.Collapsed;
        ChapterInfoTextNarrow.Visibility = Visibility.Collapsed;
    }

    private void BuildMetadataPanel()
    {
        if (_audioBook == null) return;

        MetadataItemsPanel.Children.Clear();

        AddMetadataRow("Title", _audioBook.Title);
        AddMetadataRow("Author", _audioBook.Author);

        if (!string.IsNullOrEmpty(_audioBook.Narrator))
            AddMetadataRow("Narrator", _audioBook.Narrator);

        if (!string.IsNullOrEmpty(_audioBook.SeriesName))
        {
            var seriesDisplay = _audioBook.SeriesName;
            if (!string.IsNullOrEmpty(_audioBook.SeriesSequence))
                seriesDisplay += $" (Book {_audioBook.SeriesSequence})";
            AddMetadataRow("Series", seriesDisplay);
        }

        AddMetadataRow("Duration", FormatDuration(_audioBook.Duration));

        if (_audioBook.AudioFiles.Count > 0)
        {
            var totalSize = _audioBook.AudioFiles.Sum(f => f.Size);
            if (totalSize > 0)
                AddMetadataRow("Size", FormatSize(totalSize));
            AddMetadataRow("Files", $"{_audioBook.AudioFiles.Count} audio file{(_audioBook.AudioFiles.Count > 1 ? "s" : "")}");
        }

        if (_audioBook.AddedAt.HasValue)
            AddMetadataRow("Added", _audioBook.AddedAt.Value.ToString("MMMM d, yyyy"));

        if (_audioBook.IsFinished)
            AddMetadataRow("Status", "Finished");
        else if (_audioBook.ProgressPercent > 0)
            AddMetadataRow("Status", $"{(int)_audioBook.ProgressPercent}% complete");
    }

    private void AddMetadataRow(string label, string value)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var labelBlock = new TextBlock
        {
            Text = label,
            Foreground = (Brush)Application.Current.Resources["MistFaintBrush"],
            FontSize = 13
        };
        Grid.SetColumn(labelBlock, 0);

        var valueBlock = new TextBlock
        {
            Text = value,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["StarlightDimBrush"]
        };
        Grid.SetColumn(valueBlock, 1);

        grid.Children.Add(labelBlock);
        grid.Children.Add(valueBlock);
        MetadataItemsPanel.Children.Add(grid);
    }

    private void LoadChapters()
    {
        if (_audioBook?.Chapters == null || _audioBook.Chapters.Count == 0)
        {
            ChaptersPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var currentIdx = _audioBook.GetCurrentChapterIndex();
        var currentTimeSeconds = _audioBook.CurrentTime.TotalSeconds;

        ChaptersHeader.Text = $"Chapters ({_audioBook.Chapters.Count})";
        ChaptersPanel.Visibility = Visibility.Visible;

        var chapterItems = _audioBook.Chapters.Select((ch, idx) =>
        {
            // Determine chapter completion status
            double progressPercent = 0;
            string status;
            if (currentTimeSeconds >= ch.End)
            {
                progressPercent = 100;
                status = "done";
            }
            else if (idx == currentIdx && currentTimeSeconds >= ch.Start)
            {
                progressPercent = Math.Clamp((currentTimeSeconds - ch.Start) / (ch.End - ch.Start) * 100.0, 0, 100);
                status = "current";
            }
            else
            {
                status = "upcoming";
            }

            return new ChapterDisplayItem
            {
                Index = idx + 1,
                Title = ch.Title,
                StartTime = FormatDuration(ch.StartTime),
                Duration = FormatDuration(ch.Duration),
                IsCurrent = idx == currentIdx,
                ProgressPercent = progressPercent,
                Status = status
            };
        }).ToList();

        ChaptersRepeater.ItemTemplate = CreateChapterTemplate();
        ChaptersRepeater.ItemsSource = chapterItems;
    }

    private DataTemplate CreateChapterTemplate()
    {
        var template = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(@"
            <DataTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"">
                <Grid CornerRadius=""4"" Padding=""12,8""
                      Background=""{Binding ChapterBackground}"">
                    <Grid.RowDefinitions>
                        <RowDefinition Height=""Auto""/>
                        <RowDefinition Height=""Auto""/>
                    </Grid.RowDefinitions>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width=""40""/>
                        <ColumnDefinition Width=""*""/>
                        <ColumnDefinition Width=""Auto""/>
                        <ColumnDefinition Width=""Auto""/>
                    </Grid.ColumnDefinitions>
                    <TextBlock Grid.Column=""0"" Text=""{Binding Index}"" Foreground=""#FF6B7280""/>
                    <TextBlock Grid.Column=""1"" Text=""{Binding Title}"" TextTrimming=""CharacterEllipsis""
                               FontWeight=""{Binding TitleWeight}"" Foreground=""#FFE0E0E8""/>
                    <TextBlock Grid.Column=""2"" Text=""{Binding StatusIcon}"" Margin=""8,0,0,0"" FontSize=""12""
                               Foreground=""#FFC5A55A""/>
                    <TextBlock Grid.Column=""3"" Text=""{Binding StartTime}"" Foreground=""#FF6B7280"" Margin=""8,0,0,0""/>
                    <ProgressBar Grid.Row=""1"" Grid.ColumnSpan=""4""
                                 Minimum=""0"" Maximum=""100""
                                 Value=""{Binding ProgressPercent}""
                                 Height=""3"" Margin=""0,4,0,0""
                                 CornerRadius=""2""
                                 Foreground=""#FFC5A55A""
                                 Visibility=""{Binding ProgressBarVisibility}""/>
                </Grid>
            </DataTemplate>");
        return template;
    }

    private void LoadTracks()
    {
        if (_audioBook?.AudioFiles == null || _audioBook.AudioFiles.Count == 0)
        {
            TracksPanel.Visibility = Visibility.Collapsed;
            return;
        }

        TracksHeader.Text = $"Tracks ({_audioBook.AudioFiles.Count})";

        var trackItems = _audioBook.AudioFiles.Select((af, idx) => new TrackItem
        {
            Index = idx + 1,
            Title = af.Filename,
            Duration = FormatDuration(af.Duration),
            IsDownloaded = !string.IsNullOrEmpty(af.LocalPath)
        }).ToList();

        TracksRepeater.ItemTemplate = CreateTrackTemplate();
        TracksRepeater.ItemsSource = trackItems;
    }

    private DataTemplate CreateTrackTemplate()
    {
        var template = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(@"
            <DataTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"">
                <Grid Background=""#FF111827""
                      CornerRadius=""4""
                      Padding=""12,8"">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width=""40""/>
                        <ColumnDefinition Width=""*""/>
                        <ColumnDefinition Width=""Auto""/>
                    </Grid.ColumnDefinitions>
                    <TextBlock Grid.Column=""0"" Text=""{Binding Index}"" Foreground=""#FF6B7280""/>
                    <TextBlock Grid.Column=""1"" Text=""{Binding Title}"" TextTrimming=""CharacterEllipsis""
                               Foreground=""#FFE0E0E8""/>
                    <TextBlock Grid.Column=""2"" Text=""{Binding Duration}"" Foreground=""#FF6B7280"" Margin=""8,0,0,0""/>
                </Grid>
            </DataTemplate>");
        return template;
    }

    private void UpdateDownloadState()
    {
        if (_audioBook == null) return;

        if (_audioBook.IsDownloaded)
        {
            DownloadedBadge.Visibility = Visibility.Visible;
            DownloadedBadgeNarrow.Visibility = Visibility.Visible;
            DownloadIcon.Glyph = "\uE74D";
            DownloadButtonText.Text = "Remove Download";
        }
        else
        {
            DownloadedBadge.Visibility = Visibility.Collapsed;
            DownloadedBadgeNarrow.Visibility = Visibility.Collapsed;
            DownloadIcon.Glyph = "\uE896";
            DownloadButtonText.Text = "Download";
        }
    }

    private static string FormatDuration(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        return $"{ts.Minutes}m {ts.Seconds}s";
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:F1} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} bytes";
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }

    private void AuthorLink_Click(object sender, RoutedEventArgs e)
    {
        if (_audioBook == null) return;

        // Navigate back to library and set author filter
        _libraryViewModel.CurrentViewMode = ViewMode.Author;
        _libraryViewModel.SelectedGroupFilter = _audioBook.Author;

        if (Frame.CanGoBack)
            Frame.GoBack();
    }

    private void SeriesLink_Click(object sender, RoutedEventArgs e)
    {
        if (_audioBook?.SeriesName == null) return;

        // Navigate back to library and set series filter
        _libraryViewModel.CurrentViewMode = ViewMode.Series;
        _libraryViewModel.SelectedGroupFilter = _audioBook.SeriesName;

        if (Frame.CanGoBack)
            Frame.GoBack();
    }

    private async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_audioBook == null) return;

        _logger.Log($"Play button clicked for: {_audioBook.Title}");

        try
        {
            PlayButton.IsEnabled = false;
            DownloadButton.IsEnabled = false;
            PlayIcon.Glyph = "\uE916"; // Loading icon
            PlayButtonText.Text = "Loading stream...";

            var loaded = await _playbackService.LoadAudioBookAsync(_audioBook);
            if (loaded)
            {
                await _playbackService.PlayAsync();
                _logger.Log("Playback started, navigating to player page");
                Frame.Navigate(typeof(PlayerPage));
            }
            else
            {
                _logger.LogWarning("Failed to load audiobook for playback");
                PlayButtonText.Text = "Failed - try again";
                PlayIcon.Glyph = "\uE768";
                await Task.Delay(3000);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Error playing audiobook", ex);
            PlayButtonText.Text = $"Error: {ex.Message}";
            PlayIcon.Glyph = "\uE768";
            await Task.Delay(3000);
        }
        finally
        {
            PlayButton.IsEnabled = true;
            DownloadButton.IsEnabled = true;
            PlayIcon.Glyph = "\uE768";
            if (_audioBook != null)
                PlayButtonText.Text = _audioBook.ProgressPercent > 0 ? "Continue" : "Play";
        }
    }

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_audioBook == null) return;

        try
        {
            DownloadButton.IsEnabled = false;

            if (_audioBook.IsDownloaded)
            {
                DownloadButtonText.Text = "Removing...";
                await _downloadService.DeleteDownloadAsync(_audioBook.Id);
                _audioBook.IsDownloaded = false;
                _audioBook.LocalPath = null;
                foreach (var af in _audioBook.AudioFiles) af.LocalPath = null;
                _logger.Log($"Removed download for: {_audioBook.Title}");
            }
            else
            {
                DownloadButtonText.Text = "Starting download...";
                _logger.Log($"Starting download for: {_audioBook.Title}, AudioFiles: {_audioBook.AudioFiles.Count}");

                // Subscribe to download events
                _downloadService.DownloadProgressChanged += OnDownloadProgress;
                _downloadService.DownloadCompleted += OnDownloadCompleted;
                _downloadService.DownloadFailed += OnDownloadFailed;

                var downloadItem = await _downloadService.QueueDownloadAsync(_audioBook);
                DownloadButtonText.Text = "Downloading...";
                _logger.Log($"Queued download for: {_audioBook.Title}, DownloadId: {downloadItem.Id}");
            }

            UpdateDownloadState();
        }
        catch (Exception ex)
        {
            _logger.LogError("Error with download action", ex);
            DownloadButtonText.Text = $"Error: {ex.Message}";
            await Task.Delay(3000);
            UpdateDownloadState();
        }
        finally
        {
            DownloadButton.IsEnabled = true;
        }
    }

    private void OnDownloadProgress(object? sender, Services.DownloadProgressEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var pct = Math.Min((int)e.Progress, 100); // e.Progress is already 0-100
            DownloadButtonText.Text = $"Downloading... {pct}%";
        });
    }

    private void OnDownloadCompleted(object? sender, DownloadItem e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _logger.Log($"Download completed for: {e.Title}");
            if (_audioBook != null)
                _audioBook.IsDownloaded = true;
            UpdateDownloadState();

            // Unsubscribe
            _downloadService.DownloadProgressChanged -= OnDownloadProgress;
            _downloadService.DownloadCompleted -= OnDownloadCompleted;
            _downloadService.DownloadFailed -= OnDownloadFailed;
        });
    }

    private void OnDownloadFailed(object? sender, DownloadItem e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _logger.LogWarning($"Download failed for: {e.Title} - {e.ErrorMessage}");
            DownloadButtonText.Text = $"Failed: {e.ErrorMessage}";

            // Unsubscribe
            _downloadService.DownloadProgressChanged -= OnDownloadProgress;
            _downloadService.DownloadCompleted -= OnDownloadCompleted;
            _downloadService.DownloadFailed -= OnDownloadFailed;
        });
    }

    private class TrackItem
    {
        public int Index { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Duration { get; set; } = string.Empty;
        public bool IsDownloaded { get; set; }
    }

    private class ChapterDisplayItem
    {
        public int Index { get; set; }
        public string Title { get; set; } = string.Empty;
        public string StartTime { get; set; } = string.Empty;
        public string Duration { get; set; } = string.Empty;
        public bool IsCurrent { get; set; }
        public double ProgressPercent { get; set; }
        public string Status { get; set; } = "upcoming"; // "done", "current", "upcoming"

        public string StatusIcon => Status switch
        {
            "done" => "\u2713",     // checkmark
            "current" => "\u25B6",  // play triangle
            _ => ""
        };

        public string TitleWeight => IsCurrent ? "SemiBold" : "Normal";

        public string ChapterBackground => IsCurrent ? "#1AC5A55A" : "Transparent";

        public Microsoft.UI.Xaml.Visibility ProgressBarVisibility =>
            Status == "current" ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    }
}
