using NineLivesAudio.Models;

namespace NineLivesAudio.Services;

public interface ISettingsService
{
    AppSettings Settings { get; }
    Task LoadSettingsAsync();
    Task SaveSettingsAsync();
    Task<string?> GetAuthTokenAsync();
    Task SaveAuthTokenAsync(string token);
    Task ClearAuthTokenAsync();

    // Settings Doctor metadata
    string SettingsFilePath { get; }
    string SettingsSource { get; }
    string TokenSource { get; }
    DateTime? LastLoadedAt { get; }
}
