using NineLivesAudio.Data;
using NineLivesAudio.Models;
using NineLivesAudio.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NineLivesAudio.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IAudioBookshelfApiService _apiService;
    private readonly ILocalDatabase _database;
    private readonly ILoggingService _logger;
    private readonly ISyncService _syncService;

    [ObservableProperty]
    private string _serverUrl = string.Empty;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    private string _connectionStatus = "Not connected";

    [ObservableProperty]
    private string _downloadPath = string.Empty;

    [ObservableProperty]
    private bool _autoDownloadCovers;

    [ObservableProperty]
    private double _playbackSpeed;

    [ObservableProperty]
    private bool _autoSyncProgress;

    [ObservableProperty]
    private int _syncIntervalMinutes;

    [ObservableProperty]
    private bool _startMinimized;

    [ObservableProperty]
    private bool _minimizeToTray;

    [ObservableProperty]
    private double _volume;

    [ObservableProperty]
    private string _theme = "System";

    // Security
    [ObservableProperty]
    private bool _allowSelfSignedCertificates;

    // Diagnostics
    [ObservableProperty]
    private bool _diagnosticsMode;

    // Settings Doctor
    [ObservableProperty]
    private string _settingsFilePath = string.Empty;

    [ObservableProperty]
    private string _settingsSource = string.Empty;

    [ObservableProperty]
    private string _tokenSource = string.Empty;

    [ObservableProperty]
    private string _lastLoadedAt = string.Empty;

    [ObservableProperty]
    private string _sessionId = string.Empty;

    [ObservableProperty]
    private string _serverHost = string.Empty;

    // Messages
    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private bool _hasSuccess;

    public SettingsViewModel(
        ISettingsService settingsService,
        IAudioBookshelfApiService apiService,
        ILocalDatabase database,
        ILoggingService loggingService,
        ISyncService syncService)
    {
        _settingsService = settingsService;
        _apiService = apiService;
        _database = database;
        _logger = loggingService;
        _syncService = syncService;
    }

    public async Task InitializeAsync()
    {
        await _settingsService.LoadSettingsAsync();
        LoadSettingsToUI();
        LoadDoctorInfo();

        // Check if already connected
        IsConnected = _apiService.IsAuthenticated;
        if (IsConnected)
        {
            ConnectionStatus = $"Connected to {_apiService.ServerUrl}";
        }
    }

    private void LoadSettingsToUI()
    {
        var settings = _settingsService.Settings;

        ServerUrl = settings.ServerUrl ?? string.Empty;
        Username = settings.Username ?? string.Empty;
        DownloadPath = settings.DownloadPath ?? GetDefaultDownloadPath();
        AutoDownloadCovers = settings.AutoDownloadCovers;
        PlaybackSpeed = settings.PlaybackSpeed;
        AutoSyncProgress = settings.AutoSyncProgress;
        SyncIntervalMinutes = settings.SyncIntervalMinutes;
        StartMinimized = settings.StartMinimized;
        MinimizeToTray = settings.MinimizeToTray;
        Volume = settings.Volume;
        Theme = settings.Theme ?? "System";
        AllowSelfSignedCertificates = settings.AllowSelfSignedCertificates;
        DiagnosticsMode = settings.DiagnosticsMode;
    }

    private void LoadDoctorInfo()
    {
        SettingsFilePath = _settingsService.SettingsFilePath;
        SettingsSource = _settingsService.SettingsSource;
        TokenSource = _settingsService.TokenSource;
        LastLoadedAt = _settingsService.LastLoadedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Never";
        SessionId = _logger.SessionId;

        if (!string.IsNullOrEmpty(_settingsService.Settings.ServerUrl))
        {
            try { ServerHost = new Uri(_settingsService.Settings.ServerUrl).Host; }
            catch { ServerHost = _settingsService.Settings.ServerUrl; }
        }
        else
        {
            ServerHost = "(not configured)";
        }
    }

    private static string GetDefaultDownloadPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            "AudioBookshelf");
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(ServerUrl))
        {
            ErrorMessage = "Please enter a server URL";
            HasError = true;
            return;
        }

        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Please enter username and password";
            HasError = true;
            return;
        }

        try
        {
            IsConnecting = true;
            HasError = false;
            HasSuccess = false;
            ConnectionStatus = "Connecting...";

            var success = await _apiService.LoginAsync(ServerUrl, Username, Password);

            if (success)
            {
                IsConnected = true;
                ConnectionStatus = $"Connected to {ServerUrl}";
                SuccessMessage = "Successfully connected!";
                HasSuccess = true;
                Password = string.Empty; // Clear password from UI

                await SaveSettingsAsync();
                LoadDoctorInfo();
            }
            else
            {
                IsConnected = false;
                ConnectionStatus = "Connection failed";
                ErrorMessage = _apiService.LastError ?? "Login failed. Check your credentials and server URL.";
                HasError = true;
            }
        }
        catch (Exception ex)
        {
            IsConnected = false;
            ConnectionStatus = "Connection failed";
            ErrorMessage = $"Connection error: {ex.Message}";
            HasError = true;
        }
        finally
        {
            IsConnecting = false;
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        try
        {
            await _apiService.LogoutAsync();
            IsConnected = false;
            ConnectionStatus = "Not connected";
            SuccessMessage = "Disconnected successfully";
            HasSuccess = true;
            LoadDoctorInfo();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error disconnecting: {ex.Message}";
            HasError = true;
        }
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (!IsConnected)
        {
            ErrorMessage = "Not connected. Please connect first.";
            HasError = true;
            return;
        }

        try
        {
            HasError = false;
            HasSuccess = false;

            var valid = await _apiService.ValidateTokenAsync();

            if (valid)
            {
                SuccessMessage = "Connection test successful!";
                HasSuccess = true;
            }
            else
            {
                ErrorMessage = "Connection test failed. Token may be expired.";
                HasError = true;
                IsConnected = false;
                ConnectionStatus = "Disconnected (token expired)";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Connection test error: {ex.Message}";
            HasError = true;
        }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        try
        {
            HasError = false;
            HasSuccess = false;

            var settings = _settingsService.Settings;

            settings.ServerUrl = ServerUrl;
            settings.Username = Username;
            settings.DownloadPath = DownloadPath;
            settings.AutoDownloadCovers = AutoDownloadCovers;
            settings.PlaybackSpeed = PlaybackSpeed;
            settings.AutoSyncProgress = AutoSyncProgress;
            settings.SyncIntervalMinutes = SyncIntervalMinutes;
            settings.StartMinimized = StartMinimized;
            settings.MinimizeToTray = MinimizeToTray;
            settings.Volume = Volume;
            settings.Theme = Theme;
            settings.AllowSelfSignedCertificates = AllowSelfSignedCertificates;
            settings.DiagnosticsMode = DiagnosticsMode;

            await _settingsService.SaveSettingsAsync();

            SuccessMessage = "Settings saved successfully";
            HasSuccess = true;

            LoadDoctorInfo();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to save settings: {ex.Message}";
            HasError = true;
        }
    }

    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        try
        {
            HasError = false;
            HasSuccess = false;

            await _database.ClearAllDataAsync();

            SuccessMessage = "Cache cleared successfully";
            HasSuccess = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to clear cache: {ex.Message}";
            HasError = true;
        }
    }

    [RelayCommand]
    private async Task ManualSyncAsync()
    {
        try
        {
            HasError = false;
            HasSuccess = false;

            if (!_apiService.IsAuthenticated)
            {
                ErrorMessage = "Not connected to server. Connect first.";
                HasError = true;
                return;
            }

            SuccessMessage = "Syncing...";
            HasSuccess = true;

            await _syncService.SyncNowAsync();

            SuccessMessage = "Sync completed successfully";
            HasSuccess = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Sync failed: {ex.Message}";
            HasError = true;
        }
    }

    [RelayCommand]
    private async Task ResetConnectionAsync()
    {
        try
        {
            HasError = false;
            HasSuccess = false;

            await _apiService.LogoutAsync();
            await _settingsService.ClearAuthTokenAsync();

            IsConnected = false;
            ConnectionStatus = "Not connected";
            SuccessMessage = "Connection reset. Token cleared.";
            HasSuccess = true;

            LoadDoctorInfo();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Reset failed: {ex.Message}";
            HasError = true;
        }
    }

    [RelayCommand]
    private async Task ExportLogsAsync()
    {
        try
        {
            HasError = false;
            HasSuccess = false;
            var zipPath = await _logger.ExportLogsAsync();
            SuccessMessage = $"Logs exported to: {zipPath}";
            HasSuccess = true;

            // Open the containing folder
            var dir = Path.GetDirectoryName(zipPath);
            if (!string.IsNullOrEmpty(dir))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to export logs: {ex.Message}";
            HasError = true;
        }
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NineLivesAudio", "Logs");
            Directory.CreateDirectory(logDir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = logDir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not open log folder: {ex.Message}";
            HasError = true;
        }
    }

    [RelayCommand]
    private void OpenSettingsFolder()
    {
        try
        {
            var dir = Path.GetDirectoryName(_settingsService.SettingsFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not open settings folder: {ex.Message}";
            HasError = true;
        }
    }

    [RelayCommand]
    private void DismissError()
    {
        HasError = false;
        ErrorMessage = string.Empty;
    }

    [RelayCommand]
    private void DismissSuccess()
    {
        HasSuccess = false;
        SuccessMessage = string.Empty;
    }
}
