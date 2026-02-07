using AudioBookshelfApp.Models;
using AudioBookshelfApp.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace AudioBookshelfApp.Controls;

public sealed partial class MiniPlayer : UserControl
{
    private readonly IAudioPlaybackService _playbackService;
    private bool _isPlaying;

    public MiniPlayer()
    {
        this.InitializeComponent();

        _playbackService = App.Services.GetRequiredService<IAudioPlaybackService>();

        _playbackService.PlaybackStateChanged += OnPlaybackStateChanged;
        _playbackService.PositionChanged += OnPositionChanged;

        UpdateUI();
    }

    private void OnPlaybackStateChanged(object? sender, PlaybackStateChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _isPlaying = e.State == PlaybackState.Playing;
            UpdateUI();
        });
    }

    private void OnPositionChanged(object? sender, TimeSpan position)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var duration = _playbackService.Duration;
            if (duration.TotalSeconds > 0)
            {
                ProgressBar.Value = position.TotalSeconds / duration.TotalSeconds * 100;
            }
        });
    }

    private void UpdateUI()
    {
        var audioBook = _playbackService.CurrentAudioBook;

        if (audioBook != null)
        {
            TitleText.Text = audioBook.Title;
            AuthorText.Text = audioBook.Author;

            if (!string.IsNullOrEmpty(audioBook.CoverPath))
            {
                try
                {
                    CoverImage.Source = new BitmapImage(new Uri(audioBook.CoverPath));
                }
                catch
                {
                    CoverImage.Source = null;
                }
            }
            else
            {
                CoverImage.Source = null;
            }
        }
        else
        {
            TitleText.Text = "No book playing";
            AuthorText.Text = "";
            CoverImage.Source = null;
            ProgressBar.Value = 0;
        }

        PlayPauseIcon.Glyph = _isPlaying ? "\uE769" : "\uE768";
    }

    private async void BtnPlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_isPlaying)
        {
            await _playbackService.PauseAsync();
        }
        else
        {
            await _playbackService.PlayAsync();
        }
    }

    private async void BtnRewind_Click(object sender, RoutedEventArgs e)
    {
        var newPosition = _playbackService.Position - TimeSpan.FromSeconds(10);
        if (newPosition < TimeSpan.Zero)
            newPosition = TimeSpan.Zero;
        await _playbackService.SeekAsync(newPosition);
    }

    private async void BtnForward_Click(object sender, RoutedEventArgs e)
    {
        var newPosition = _playbackService.Position + TimeSpan.FromSeconds(30);
        if (newPosition > _playbackService.Duration)
            newPosition = _playbackService.Duration;
        await _playbackService.SeekAsync(newPosition);
    }
}
