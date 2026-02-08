using NineLivesAudio.Data;
using NineLivesAudio.Models;
using NineLivesAudio.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace NineLivesAudio.ViewModels;

public enum ViewMode
{
    All,
    Series,
    Author,
    Genre
}

public enum SortMode
{
    Default,
    Title,
    Author,
    Progress,
    RecentProgress
}

public partial class LibraryViewModel : ObservableObject
{
    private readonly IAudioBookshelfApiService _apiService;
    private readonly ILocalDatabase _database;
    private readonly IAudioPlaybackService _playbackService;
    private readonly IDownloadService _downloadService;
    private readonly ISyncService _syncService;
    private readonly ILoggingService _logger;
    private readonly INotificationService _notifications;
    private readonly IMetadataNormalizer _normalizer;

    // Debounce timer for search
    private CancellationTokenSource? _searchDebounceToken;
    private const int SearchDebounceMs = 300;

    // Cache for normalized metadata (avoid recomputing on every render)
    private readonly Dictionary<string, NormalizedMetadata> _normalizedCache = new();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private Library? _selectedLibrary;

    [ObservableProperty]
    private AudioBook? _selectedAudioBook;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private bool _showEmptyState;

    [ObservableProperty]
    private string _emptyStateTitle = "No Books Found";

    [ObservableProperty]
    private string _emptyStateSubtitle = "Connect to your server or refresh to load your library.";

    [ObservableProperty]
    private ViewMode _currentViewMode = ViewMode.All;

    [ObservableProperty]
    private string? _selectedGroupFilter;

    [ObservableProperty]
    private SortMode _currentSortMode = SortMode.Default;

    [ObservableProperty]
    private bool _hideFinished;

    public ObservableCollection<Library> Libraries { get; } = new();
    public ObservableCollection<AudioBook> AudioBooks { get; } = new();
    public ObservableCollection<AudioBook> FilteredAudioBooks { get; } = new();
    public ObservableCollection<string> AvailableGroups { get; } = new();

    public LibraryViewModel(
        IAudioBookshelfApiService apiService,
        ILocalDatabase database,
        IAudioPlaybackService playbackService,
        IDownloadService downloadService,
        ISyncService syncService,
        ILoggingService logger,
        INotificationService notifications,
        IMetadataNormalizer normalizer)
    {
        _apiService = apiService;
        _database = database;
        _playbackService = playbackService;
        _downloadService = downloadService;
        _syncService = syncService;
        _logger = logger;
        _notifications = notifications;
        _normalizer = normalizer;

        // Subscribe to download events for toasts
        _downloadService.DownloadCompleted += OnDownloadCompleted;
        _downloadService.DownloadFailed += OnDownloadFailed;
    }

    /// <summary>
    /// Gets normalized metadata for an audiobook (cached).
    /// </summary>
    public NormalizedMetadata GetNormalized(AudioBook book)
    {
        if (!_normalizedCache.TryGetValue(book.Id, out var cached))
        {
            cached = _normalizer.Normalize(book);
            _normalizedCache[book.Id] = cached;
        }
        return cached;
    }

    public async Task InitializeAsync()
    {
        try
        {
            // Try to restore auth state if not already authenticated
            if (!_apiService.IsAuthenticated)
            {
                _logger.Log("API not authenticated, attempting to validate saved token...");
                var validated = await _apiService.ValidateTokenAsync();
                _logger.Log($"Token validation result: {validated}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to validate token", ex);
        }

        await LoadLibrariesAsync();
    }

    [RelayCommand]
    private async Task LoadLibrariesAsync()
    {
        try
        {
            _logger.Log("LoadLibrariesAsync started");
            IsLoading = true;
            HasError = false;

            // Try to load from local database first
            _logger.Log("Loading libraries from local database...");
            var localLibraries = await _database.GetAllLibrariesAsync();
            _logger.Log($"Loaded {localLibraries.Count} libraries from database");

            Libraries.Clear();
            foreach (var library in localLibraries)
            {
                Libraries.Add(library);
            }

            // If online, refresh from server
            if (_apiService.IsAuthenticated)
            {
                _logger.Log("Fetching libraries from server...");
                var serverLibraries = await _apiService.GetLibrariesAsync();
                _logger.Log($"Received {serverLibraries.Count} libraries from server");

                if (serverLibraries.Any())
                {
                    await _database.SaveLibrariesAsync(serverLibraries);

                    Libraries.Clear();
                    foreach (var library in serverLibraries)
                    {
                        Libraries.Add(library);
                    }
                }
            }

            // Select first library if none selected
            if (SelectedLibrary == null && Libraries.Any())
            {
                _logger.Log($"Auto-selecting first library: {Libraries.First().Name}");
                SelectedLibrary = Libraries.First();
            }

            _logger.Log("LoadLibrariesAsync completed");
        }
        catch (Exception ex)
        {
            _logger.LogError("LoadLibrariesAsync failed", ex);
            ErrorMessage = $"Failed to load libraries: {ex.Message}";
            HasError = true;
            _notifications.ShowError(ex.Message, "Load Failed");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task LoadAudioBooksAsync()
    {
        if (SelectedLibrary == null)
        {
            _logger.LogWarning("LoadAudioBooksAsync called but no library selected");
            return;
        }

        try
        {
            _logger.Log($"LoadAudioBooksAsync started for library: {SelectedLibrary.Name}");
            IsLoading = true;
            HasError = false;

            // Clear normalized cache when loading fresh data
            _normalizedCache.Clear();

            // Load from local database first
            _logger.Log("Loading audiobooks from local database...");
            var localBooks = await _database.GetAllAudioBooksAsync();
            _logger.Log($"Loaded {localBooks.Count} audiobooks from database");

            AudioBooks.Clear();
            foreach (var book in localBooks)
            {
                AudioBooks.Add(book);
            }

            // If online, refresh from server
            if (_apiService.IsAuthenticated)
            {
                _logger.Log($"Fetching audiobooks from server for library {SelectedLibrary.Id}...");
                var serverBooks = await _apiService.GetLibraryItemsAsync(SelectedLibrary.Id);
                _logger.Log($"Received {serverBooks.Count} audiobooks from server");

                if (serverBooks.Any())
                {
                    // Preserve local state (download info + progress) from DB
                    _logger.Log("Merging local state...");
                    int progressMerged = 0;
                    foreach (var serverBook in serverBooks)
                    {
                        var localBook = localBooks.FirstOrDefault(b => b.Id == serverBook.Id);
                        if (localBook != null)
                        {
                            serverBook.IsDownloaded = localBook.IsDownloaded;
                            serverBook.LocalPath = localBook.LocalPath;

                            // Preserve progress — server library endpoint doesn't include it
                            if (localBook.Progress > 0 || localBook.CurrentTime.TotalSeconds > 0)
                            {
                                serverBook.CurrentTime = localBook.CurrentTime;
                                serverBook.Progress = localBook.Progress;
                                serverBook.IsFinished = localBook.IsFinished;
                                progressMerged++;
                            }

                            foreach (var af in serverBook.AudioFiles)
                            {
                                var localFile = localBook.AudioFiles.FirstOrDefault(f => f.Ino == af.Ino);
                                if (localFile != null)
                                {
                                    af.LocalPath = localFile.LocalPath;
                                }
                            }
                        }
                    }

                    var booksWithProgress = serverBooks.Count(b => b.Progress > 0);
                    _logger.Log($"Merged progress for {progressMerged} books ({booksWithProgress} have Progress>0)");
                    _logger.Log("Saving audiobooks to database...");
                    await _database.SaveAudioBooksAsync(serverBooks);

                    _logger.Log("Updating UI with audiobooks...");
                    AudioBooks.Clear();
                    foreach (var book in serverBooks)
                    {
                        AudioBooks.Add(book);
                    }
                }
            }

            _logger.Log("Updating available groups...");
            UpdateAvailableGroups();
            _logger.Log("Applying filter...");
            ApplyFilterInternal();
            _logger.Log($"LoadAudioBooksAsync completed. Total books: {AudioBooks.Count}, Filtered: {FilteredAudioBooks.Count}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"LoadAudioBooksAsync failed for library {SelectedLibrary?.Name}", ex);
            ErrorMessage = $"Failed to load audiobooks: {ex.Message}";
            HasError = true;
        }
        finally
        {
            IsLoading = false;
            UpdateShowEmptyState();
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        await _syncService.SyncNowAsync();
        await LoadLibrariesAsync();
        if (SelectedLibrary != null)
        {
            await LoadAudioBooksAsync();
        }
        IsRefreshing = false;
        _notifications.ShowSuccess("Library refreshed");
    }

    [RelayCommand]
    private async Task PlayAudioBookAsync(AudioBook? audioBook)
    {
        if (audioBook == null) return;

        try
        {
            var loaded = await _playbackService.LoadAudioBookAsync(audioBook);
            if (loaded)
            {
                await _playbackService.PlayAsync();
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to play audiobook: {ex.Message}";
            HasError = true;
            _notifications.ShowError(ex.Message, "Playback Error");
        }
    }

    [RelayCommand]
    private async Task DownloadAudioBookAsync(AudioBook? audioBook)
    {
        if (audioBook == null) return;

        try
        {
            await _downloadService.QueueDownloadAsync(audioBook);
            _notifications.ShowInfo($"Downloading: {audioBook.Title}");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to queue download: {ex.Message}";
            HasError = true;
            _notifications.ShowError(ex.Message, "Download Error");
        }
    }

    [RelayCommand]
    private async Task RemoveDownloadAsync(AudioBook? audioBook)
    {
        if (audioBook == null) return;

        try
        {
            await _downloadService.DeleteDownloadAsync(audioBook.Id);
            audioBook.IsDownloaded = false;
            audioBook.LocalPath = null;
            foreach (var af in audioBook.AudioFiles) af.LocalPath = null;
            _normalizedCache.Remove(audioBook.Id);
            _notifications.ShowSuccess($"Download removed: {audioBook.Title}");
        }
        catch (Exception ex)
        {
            _notifications.ShowError(ex.Message, "Remove Download Error");
        }
    }

    private void OnDownloadCompleted(object? sender, DownloadItem item)
    {
        _notifications.ShowSuccess($"Download complete: {item.Title}");

        // Update the AudioBook in our collection
        var book = AudioBooks.FirstOrDefault(b => b.Id == item.AudioBookId);
        if (book != null)
        {
            book.IsDownloaded = true;
            // Clear cache to force re-render
            _normalizedCache.Remove(book.Id);
        }
    }

    private void OnDownloadFailed(object? sender, DownloadItem item)
    {
        _notifications.ShowError($"{item.Title}: {item.ErrorMessage}", "Download Failed");
    }

    partial void OnSelectedLibraryChanged(Library? value)
    {
        if (value != null)
        {
            _ = LoadAudioBooksAsync();
        }
    }

    partial void OnSearchQueryChanged(string value)
    {
        // Debounce search to reduce filtering churn
        _searchDebounceToken?.Cancel();
        _searchDebounceToken = new CancellationTokenSource();
        var token = _searchDebounceToken.Token;

        // Capture dispatcher on UI thread BEFORE scheduling background continuation
        var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        Task.Delay(SearchDebounceMs, token).ContinueWith(_ =>
        {
            if (!token.IsCancellationRequested)
            {
                dispatcher?.TryEnqueue(() =>
                {
                    ApplyFilterInternal();
                });
            }
        }, TaskScheduler.Default);
    }

    partial void OnCurrentViewModeChanged(ViewMode value)
    {
        UpdateAvailableGroups();
        SelectedGroupFilter = null;
        ApplyFilterInternal();
    }

    partial void OnSelectedGroupFilterChanged(string? value)
    {
        ApplyFilterInternal();
    }

    partial void OnCurrentSortModeChanged(SortMode value)
    {
        ApplyFilterInternal();
    }

    partial void OnHideFinishedChanged(bool value)
    {
        ApplyFilterInternal();
    }

    [RelayCommand]
    private void SetViewMode(ViewMode mode)
    {
        CurrentViewMode = mode;
    }

    private void UpdateAvailableGroups()
    {
        AvailableGroups.Clear();

        IEnumerable<string> groups = CurrentViewMode switch
        {
            ViewMode.Series => AudioBooks
                .Select(b => GetNormalized(b).SeriesName)
                .Where(s => !string.IsNullOrEmpty(s))
                .Cast<string>()
                .Distinct()
                .OrderBy(s => s),
            ViewMode.Author => AudioBooks
                .Select(b => GetNormalized(b).DisplayAuthor)
                .Where(a => !string.IsNullOrEmpty(a))
                .Distinct()
                .OrderBy(a => a),
            ViewMode.Genre => AudioBooks
                .SelectMany(b => b.Genres)
                .Where(g => !string.IsNullOrEmpty(g))
                .Distinct()
                .OrderBy(g => g),
            _ => Enumerable.Empty<string>()
        };

        foreach (var group in groups)
        {
            AvailableGroups.Add(group);
        }
    }

    private void ApplyFilterInternal()
    {
        try
        {
            _logger.LogDebug($"ApplyFilter called. SearchQuery: '{SearchQuery}', ViewMode: {CurrentViewMode}, AudioBooks count: {AudioBooks.Count}");
            FilteredAudioBooks.Clear();

            // Start with all audiobooks
            var filtered = AudioBooks.AsEnumerable();

            // Apply view mode filter (use normalized metadata)
            if (CurrentViewMode != ViewMode.All && !string.IsNullOrEmpty(SelectedGroupFilter))
            {
                filtered = CurrentViewMode switch
                {
                    ViewMode.Series => filtered
                        .Where(b => GetNormalized(b).SeriesName == SelectedGroupFilter)
                        .OrderBy(b => GetNormalized(b).SeriesNumber ?? double.MaxValue),
                    ViewMode.Author => filtered
                        .Where(b => GetNormalized(b).DisplayAuthor == SelectedGroupFilter),
                    ViewMode.Genre => filtered
                        .Where(b => b.Genres.Contains(SelectedGroupFilter)),
                    _ => filtered
                };
            }

            // Apply hide-finished filter
            if (HideFinished)
            {
                filtered = filtered.Where(b => !b.IsFinished && b.Progress < 1.0);
            }

            // Apply search filter (use normalized SearchText for efficiency)
            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                var searchLower = SearchQuery.ToLowerInvariant();
                filtered = filtered.Where(b => GetNormalized(b).SearchText.Contains(searchLower));
            }

            // Apply sort
            var sorted = CurrentSortMode switch
            {
                SortMode.Title => filtered.OrderBy(b => b.Title),
                SortMode.Author => filtered.OrderBy(b => b.Author).ThenBy(b => b.Title),
                SortMode.Progress => filtered.OrderByDescending(b => b.Progress).ThenBy(b => b.Title),
                SortMode.RecentProgress => filtered
                    .OrderByDescending(b => b.Progress > 0 ? 1 : 0)
                    .ThenByDescending(b => b.CurrentTime.TotalSeconds)
                    .ThenBy(b => b.Title),
                _ => filtered // Default: server order
            };

            foreach (var book in sorted.ToList())
            {
                FilteredAudioBooks.Add(book);
            }

            UpdateShowEmptyState();
            _logger.LogDebug($"ApplyFilter completed. Filtered count: {FilteredAudioBooks.Count}");
        }
        catch (Exception ex)
        {
            _logger.LogError("ApplyFilter failed", ex);
        }
    }

    private void UpdateShowEmptyState()
    {
        ShowEmptyState = !IsLoading && FilteredAudioBooks.Count == 0;

        if (ShowEmptyState)
        {
            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                EmptyStateTitle = "No Results";
                EmptyStateSubtitle = $"No books match \"{SearchQuery}\". Try a different search.";
            }
            else if (!_apiService.IsAuthenticated)
            {
                EmptyStateTitle = "Not Connected";
                EmptyStateSubtitle = "Connect to your AudioBookshelf server in Settings.";
            }
            else if (AudioBooks.Count == 0)
            {
                EmptyStateTitle = "Library Empty";
                EmptyStateSubtitle = "Your library has no books yet. Add some on your server.";
            }
            else
            {
                EmptyStateTitle = "No Books Found";
                EmptyStateSubtitle = "Try changing your filters.";
            }
        }
    }
}
