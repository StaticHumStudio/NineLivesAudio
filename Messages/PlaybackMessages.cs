using CommunityToolkit.Mvvm.Messaging.Messages;
using NineLivesAudio.Models;
using NineLivesAudio.Services;

namespace NineLivesAudio.Messages;

/// <summary>
/// Sent when playback state changes (Playing, Paused, Stopped, etc.).
/// Replaces IAudioPlaybackService.PlaybackStateChanged event.
/// </summary>
public sealed class PlaybackStateChangedMessage : ValueChangedMessage<PlaybackStateChangedEventArgs>
{
    public PlaybackStateChangedMessage(PlaybackStateChangedEventArgs value) : base(value) { }
}

/// <summary>
/// Sent every ~500ms during playback with the current position.
/// Replaces IAudioPlaybackService.PositionChanged event.
/// </summary>
public sealed class PositionChangedMessage : ValueChangedMessage<TimeSpan>
{
    public PositionChangedMessage(TimeSpan value) : base(value) { }
}

/// <summary>
/// Sent when the current track changes in multi-file audiobook playback.
/// Replaces IAudioPlaybackService.TrackChanged event.
/// </summary>
public sealed class TrackChangedMessage : ValueChangedMessage<int>
{
    public TrackChangedMessage(int trackIndex) : base(trackIndex) { }
}

/// <summary>
/// Sent when the current chapter changes during playback.
/// Replaces IAudioPlaybackService.ChapterChanged event.
/// </summary>
public sealed class ChapterChangedMessage : ValueChangedMessage<Chapter?>
{
    public ChapterChangedMessage(Chapter? value) : base(value) { }
}
