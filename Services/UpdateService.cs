using System.Text.Json;

namespace NineLivesAudio.Services;

/// <summary>Result of asking GitHub for the newest published release.</summary>
public sealed record UpdateCheckResult(
    bool Succeeded,
    bool UpdateAvailable,
    string CurrentVersion,
    string? LatestVersion,
    string? ReleaseUrl,
    string? Error);

public interface IUpdateService
{
    Task<UpdateCheckResult> CheckForUpdateAsync();
}

/// <summary>
/// Reads the latest release of the fork that ships these builds. Deliberately read-only: the
/// user is sent to the release page rather than the app downloading and swapping its own exe.
/// </summary>
public sealed class UpdateService : IUpdateService
{
    private const string LatestReleaseApi = "https://api.github.com/repos/one-LN/NineLivesAudio/releases/latest";

    private readonly ILoggingService _logger;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public UpdateService(ILoggingService logger) => _logger = logger;

    public async Task<UpdateCheckResult> CheckForUpdateAsync()
    {
        var current = typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0);
        var currentText = $"{current.Major}.{current.Minor}.{Math.Max(current.Build, 0)}";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);

            // GitHub's API rejects requests without a user agent.
            request.Headers.UserAgent.ParseAdd($"NineLivesAudio/{currentText}");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            _logger.Log($"[Update] Checking for updates (current {currentText})");

            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var json = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);

            var root = json.RootElement;
            var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() : null;
            var url = root.TryGetProperty("html_url", out var urlElement) ? urlElement.GetString() : null;

            var latest = ParseVersion(tag);
            if (latest == null)
            {
                _logger.LogWarning($"[Update] Release tag '{tag}' is not a version — cannot compare");
                return new UpdateCheckResult(false, false, currentText, tag, url, "Unrecognised release tag");
            }

            var newer = latest > current;
            var latestText = $"{latest.Major}.{latest.Minor}.{Math.Max(latest.Build, 0)}";

            _logger.Log(newer
                ? $"[Update] Newer release available: {tag} (current {currentText})"
                : $"[Update] Already on the latest version ({currentText})");

            return new UpdateCheckResult(true, newer, currentText, tag, url, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[Update] Check failed: {ex.Message}");
            return new UpdateCheckResult(false, false, currentText, null, null, ex.Message);
        }
    }

    /// <summary>"v1.0.24" / "1.0.24" / "1.0.24-beta" → Version.</summary>
    private static Version? ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;

        var text = tag.Trim();
        if (text.StartsWith('v') || text.StartsWith('V')) text = text[1..];

        var dash = text.IndexOf('-');
        if (dash >= 0) text = text[..dash];

        return Version.TryParse(text, out var version) ? version : null;
    }
}
