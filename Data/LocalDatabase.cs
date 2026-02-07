using AudioBookshelfApp.Models;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace AudioBookshelfApp.Data;

public class LocalDatabase : ILocalDatabase, IDisposable
{
    private readonly string _dbPath;
    private SqliteConnection? _connection;
    private bool _initialized;

    public LocalDatabase()
    {
        var localFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(localFolder, "AudioBookshelfApp");
        Directory.CreateDirectory(appFolder);
        _dbPath = Path.Combine(appFolder, "audiobookshelf.db");
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;

        _connection = new SqliteConnection($"Data Source={_dbPath}");
        await _connection.OpenAsync();

        await CreateTablesAsync();
        await MigrateAsync();
        _initialized = true;
    }

    private async Task MigrateAsync()
    {
        // Add new columns if they don't exist (for existing databases)
        var columnsToAdd = new[]
        {
            ("AudioBooks", "SeriesName", "TEXT"),
            ("AudioBooks", "SeriesSequence", "TEXT"),
            ("AudioBooks", "GenresJson", "TEXT"),
            ("AudioBooks", "TagsJson", "TEXT"),
            ("AudioBooks", "ChaptersJson", "TEXT"),
        };

        foreach (var (table, column, type) in columnsToAdd)
        {
            try
            {
                var cmd = _connection!.CreateCommand();
                cmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type}";
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqliteException)
            {
                // Column already exists, ignore
            }
        }
    }

    private async Task CreateTablesAsync()
    {
        var command = _connection!.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS Libraries (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                DisplayOrder INTEGER DEFAULT 0,
                Icon TEXT DEFAULT 'audiobook',
                MediaType TEXT DEFAULT 'book',
                FoldersJson TEXT
            );

            CREATE TABLE IF NOT EXISTS AudioBooks (
                Id TEXT PRIMARY KEY,
                LibraryId TEXT,
                Title TEXT NOT NULL,
                Author TEXT,
                Narrator TEXT,
                Description TEXT,
                CoverPath TEXT,
                DurationSeconds REAL DEFAULT 0,
                AddedAt TEXT,
                AudioFilesJson TEXT,
                CurrentTimeSeconds REAL DEFAULT 0,
                Progress REAL DEFAULT 0,
                IsFinished INTEGER DEFAULT 0,
                IsDownloaded INTEGER DEFAULT 0,
                LocalPath TEXT,
                SeriesName TEXT,
                SeriesSequence TEXT,
                GenresJson TEXT,
                TagsJson TEXT,
                ChaptersJson TEXT
            );

            CREATE TABLE IF NOT EXISTS DownloadItems (
                Id TEXT PRIMARY KEY,
                AudioBookId TEXT NOT NULL,
                Title TEXT NOT NULL,
                Status INTEGER DEFAULT 0,
                TotalBytes INTEGER DEFAULT 0,
                DownloadedBytes INTEGER DEFAULT 0,
                StartedAt TEXT,
                CompletedAt TEXT,
                ErrorMessage TEXT,
                FilesToDownloadJson TEXT
            );

            CREATE TABLE IF NOT EXISTS PlaybackProgress (
                AudioBookId TEXT PRIMARY KEY,
                PositionSeconds REAL DEFAULT 0,
                IsFinished INTEGER DEFAULT 0,
                UpdatedAt TEXT
            );

            CREATE TABLE IF NOT EXISTS PendingProgressUpdates (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ItemId TEXT NOT NULL,
                CurrentTime REAL DEFAULT 0,
                IsFinished INTEGER DEFAULT 0,
                Timestamp TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_audiobooks_library ON AudioBooks(LibraryId);
            CREATE INDEX IF NOT EXISTS idx_downloads_audiobook ON DownloadItems(AudioBookId);
            CREATE INDEX IF NOT EXISTS idx_pending_item ON PendingProgressUpdates(ItemId);
        ";

        await command.ExecuteNonQueryAsync();
    }

    private void EnsureInitialized()
    {
        if (!_initialized || _connection == null)
            throw new InvalidOperationException("Database not initialized. Call InitializeAsync first.");
    }

    // AudioBooks
    public async Task<List<AudioBook>> GetAllAudioBooksAsync()
    {
        EnsureInitialized();
        var audioBooks = new List<AudioBook>();

        var command = _connection!.CreateCommand();
        command.CommandText = "SELECT * FROM AudioBooks ORDER BY Title";

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            audioBooks.Add(ReadAudioBook(reader));
        }

        return audioBooks;
    }

    public async Task<AudioBook?> GetAudioBookAsync(string id)
    {
        EnsureInitialized();

        var command = _connection!.CreateCommand();
        command.CommandText = "SELECT * FROM AudioBooks WHERE Id = @id";
        command.Parameters.AddWithValue("@id", id);

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return ReadAudioBook(reader);
        }

        return null;
    }

    public async Task SaveAudioBookAsync(AudioBook audioBook)
    {
        EnsureInitialized();

        var command = _connection!.CreateCommand();
        command.CommandText = @"
            INSERT OR REPLACE INTO AudioBooks
            (Id, Title, Author, Narrator, Description, CoverPath, DurationSeconds, AddedAt,
             AudioFilesJson, CurrentTimeSeconds, Progress, IsFinished, IsDownloaded, LocalPath,
             SeriesName, SeriesSequence, GenresJson, TagsJson, ChaptersJson)
            VALUES
            (@id, @title, @author, @narrator, @description, @coverPath, @durationSeconds, @addedAt,
             @audioFilesJson, @currentTimeSeconds, @progress, @isFinished, @isDownloaded, @localPath,
             @seriesName, @seriesSequence, @genresJson, @tagsJson, @chaptersJson)";

        AddAudioBookParameters(command, audioBook);
        await command.ExecuteNonQueryAsync();
    }

    public async Task UpdateAudioBookAsync(AudioBook audioBook)
    {
        await SaveAudioBookAsync(audioBook);
    }

    public async Task DeleteAudioBookAsync(string id)
    {
        EnsureInitialized();

        var command = _connection!.CreateCommand();
        command.CommandText = "DELETE FROM AudioBooks WHERE Id = @id";
        command.Parameters.AddWithValue("@id", id);
        await command.ExecuteNonQueryAsync();
    }

    // Libraries
    public async Task<List<Library>> GetAllLibrariesAsync()
    {
        EnsureInitialized();
        var libraries = new List<Library>();

        var command = _connection!.CreateCommand();
        command.CommandText = "SELECT * FROM Libraries ORDER BY DisplayOrder";

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            libraries.Add(ReadLibrary(reader));
        }

        return libraries;
    }

    public async Task<Library?> GetLibraryAsync(string id)
    {
        EnsureInitialized();

        var command = _connection!.CreateCommand();
        command.CommandText = "SELECT * FROM Libraries WHERE Id = @id";
        command.Parameters.AddWithValue("@id", id);

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return ReadLibrary(reader);
        }

        return null;
    }

    public async Task SaveLibraryAsync(Library library)
    {
        EnsureInitialized();

        var command = _connection!.CreateCommand();
        command.CommandText = @"
            INSERT OR REPLACE INTO Libraries (Id, Name, DisplayOrder, Icon, MediaType, FoldersJson)
            VALUES (@id, @name, @displayOrder, @icon, @mediaType, @foldersJson)";

        command.Parameters.AddWithValue("@id", library.Id);
        command.Parameters.AddWithValue("@name", library.Name);
        command.Parameters.AddWithValue("@displayOrder", library.DisplayOrder);
        command.Parameters.AddWithValue("@icon", library.Icon ?? "audiobook");
        command.Parameters.AddWithValue("@mediaType", library.MediaType ?? "book");
        command.Parameters.AddWithValue("@foldersJson", JsonSerializer.Serialize(library.Folders));

        await command.ExecuteNonQueryAsync();
    }

    public async Task UpdateLibraryAsync(Library library)
    {
        await SaveLibraryAsync(library);
    }

    public async Task DeleteLibraryAsync(string id)
    {
        EnsureInitialized();

        var command = _connection!.CreateCommand();
        command.CommandText = "DELETE FROM Libraries WHERE Id = @id";
        command.Parameters.AddWithValue("@id", id);
        await command.ExecuteNonQueryAsync();
    }

    // Downloads
    public async Task<List<DownloadItem>> GetAllDownloadItemsAsync()
    {
        EnsureInitialized();
        var downloads = new List<DownloadItem>();

        var command = _connection!.CreateCommand();
        command.CommandText = "SELECT * FROM DownloadItems ORDER BY StartedAt DESC";

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            downloads.Add(ReadDownloadItem(reader));
        }

        return downloads;
    }

    public async Task<DownloadItem?> GetDownloadItemAsync(string id)
    {
        EnsureInitialized();

        var command = _connection!.CreateCommand();
        command.CommandText = "SELECT * FROM DownloadItems WHERE Id = @id";
        command.Parameters.AddWithValue("@id", id);

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return ReadDownloadItem(reader);
        }

        return null;
    }

    public async Task SaveDownloadItemAsync(DownloadItem downloadItem)
    {
        EnsureInitialized();

        var command = _connection!.CreateCommand();
        command.CommandText = @"
            INSERT OR REPLACE INTO DownloadItems
            (Id, AudioBookId, Title, Status, TotalBytes, DownloadedBytes, StartedAt, CompletedAt, ErrorMessage, FilesToDownloadJson)
            VALUES
            (@id, @audioBookId, @title, @status, @totalBytes, @downloadedBytes, @startedAt, @completedAt, @errorMessage, @filesToDownloadJson)";

        command.Parameters.AddWithValue("@id", downloadItem.Id);
        command.Parameters.AddWithValue("@audioBookId", downloadItem.AudioBookId);
        command.Parameters.AddWithValue("@title", downloadItem.Title);
        command.Parameters.AddWithValue("@status", (int)downloadItem.Status);
        command.Parameters.AddWithValue("@totalBytes", downloadItem.TotalBytes);
        command.Parameters.AddWithValue("@downloadedBytes", downloadItem.DownloadedBytes);
        command.Parameters.AddWithValue("@startedAt", downloadItem.StartedAt?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@completedAt", downloadItem.CompletedAt?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@errorMessage", downloadItem.ErrorMessage ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@filesToDownloadJson", JsonSerializer.Serialize(downloadItem.FilesToDownload));

        await command.ExecuteNonQueryAsync();
    }

    public async Task UpdateDownloadItemAsync(DownloadItem downloadItem)
    {
        await SaveDownloadItemAsync(downloadItem);
    }

    public async Task DeleteDownloadItemAsync(string id)
    {
        EnsureInitialized();

        var command = _connection!.CreateCommand();
        command.CommandText = "DELETE FROM DownloadItems WHERE Id = @id";
        command.Parameters.AddWithValue("@id", id);
        await command.ExecuteNonQueryAsync();
    }

    // Playback Progress
    public async Task SavePlaybackProgressAsync(string audioBookId, TimeSpan position, bool isFinished)
    {
        EnsureInitialized();

        var command = _connection!.CreateCommand();
        command.CommandText = @"
            INSERT OR REPLACE INTO PlaybackProgress (AudioBookId, PositionSeconds, IsFinished, UpdatedAt)
            VALUES (@audioBookId, @positionSeconds, @isFinished, @updatedAt)";

        command.Parameters.AddWithValue("@audioBookId", audioBookId);
        command.Parameters.AddWithValue("@positionSeconds", position.TotalSeconds);
        command.Parameters.AddWithValue("@isFinished", isFinished ? 1 : 0);
        command.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync();
    }

    public async Task<(TimeSpan position, bool isFinished)?> GetPlaybackProgressAsync(string audioBookId)
    {
        EnsureInitialized();

        var command = _connection!.CreateCommand();
        command.CommandText = "SELECT PositionSeconds, IsFinished FROM PlaybackProgress WHERE AudioBookId = @audioBookId";
        command.Parameters.AddWithValue("@audioBookId", audioBookId);

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var positionSeconds = reader.GetDouble(0);
            var isFinished = reader.GetInt32(1) == 1;
            return (TimeSpan.FromSeconds(positionSeconds), isFinished);
        }

        return null;
    }

    // Offline Progress Queue
    public async Task EnqueuePendingProgressAsync(string itemId, double currentTime, bool isFinished)
    {
        EnsureInitialized();
        var command = _connection!.CreateCommand();
        command.CommandText = @"
            INSERT INTO PendingProgressUpdates (ItemId, CurrentTime, IsFinished, Timestamp)
            VALUES (@itemId, @currentTime, @isFinished, @timestamp)";
        command.Parameters.AddWithValue("@itemId", itemId);
        command.Parameters.AddWithValue("@currentTime", currentTime);
        command.Parameters.AddWithValue("@isFinished", isFinished ? 1 : 0);
        command.Parameters.AddWithValue("@timestamp", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    public async Task<List<PendingProgressEntry>> GetPendingProgressAsync()
    {
        EnsureInitialized();
        var entries = new List<PendingProgressEntry>();
        var command = _connection!.CreateCommand();
        command.CommandText = "SELECT ItemId, CurrentTime, IsFinished, Timestamp FROM PendingProgressUpdates ORDER BY Timestamp ASC";
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            entries.Add(new PendingProgressEntry
            {
                ItemId = reader.GetString(0),
                CurrentTime = reader.GetDouble(1),
                IsFinished = reader.GetInt32(2) == 1,
                Timestamp = DateTime.Parse(reader.GetString(3))
            });
        }
        return entries;
    }

    public async Task ClearPendingProgressAsync()
    {
        EnsureInitialized();
        var command = _connection!.CreateCommand();
        command.CommandText = "DELETE FROM PendingProgressUpdates";
        await command.ExecuteNonQueryAsync();
    }

    public async Task<int> GetPendingProgressCountAsync()
    {
        EnsureInitialized();
        var command = _connection!.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM PendingProgressUpdates";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    // Nine Lives (recently played)
    public async Task<List<AudioBook>> GetRecentlyPlayedAsync(int limit = 9)
    {
        EnsureInitialized();
        var audioBooks = new List<AudioBook>();

        var command = _connection!.CreateCommand();
        command.CommandText = @"
            SELECT ab.* FROM AudioBooks ab
            INNER JOIN PlaybackProgress pp ON ab.Id = pp.AudioBookId
            ORDER BY pp.UpdatedAt DESC
            LIMIT @limit";
        command.Parameters.AddWithValue("@limit", limit);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            audioBooks.Add(ReadAudioBook(reader));
        }

        return audioBooks;
    }

    // Bulk operations
    public async Task SaveAudioBooksAsync(IEnumerable<AudioBook> audioBooks)
    {
        EnsureInitialized();

        using var transaction = _connection!.BeginTransaction();
        try
        {
            foreach (var audioBook in audioBooks)
            {
                await SaveAudioBookAsync(audioBook);
            }
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task SaveLibrariesAsync(IEnumerable<Library> libraries)
    {
        EnsureInitialized();

        using var transaction = _connection!.BeginTransaction();
        try
        {
            foreach (var library in libraries)
            {
                await SaveLibraryAsync(library);
            }
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task ClearAllDataAsync()
    {
        EnsureInitialized();

        var command = _connection!.CreateCommand();
        command.CommandText = @"
            DELETE FROM AudioBooks;
            DELETE FROM Libraries;
            DELETE FROM DownloadItems;
            DELETE FROM PlaybackProgress;
        ";
        await command.ExecuteNonQueryAsync();
    }

    // Helper methods
    private static string? GetNullableString(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private AudioBook ReadAudioBook(SqliteDataReader reader)
    {
        var audioFilesJson = GetNullableString(reader, "AudioFilesJson") ?? "[]";
        var genresJson = GetNullableString(reader, "GenresJson") ?? "[]";
        var tagsJson = GetNullableString(reader, "TagsJson") ?? "[]";
        var chaptersJson = GetNullableString(reader, "ChaptersJson") ?? "[]";

        return new AudioBook
        {
            Id = reader.GetString(reader.GetOrdinal("Id")),
            Title = reader.GetString(reader.GetOrdinal("Title")),
            Author = GetNullableString(reader, "Author") ?? string.Empty,
            Narrator = GetNullableString(reader, "Narrator"),
            Description = GetNullableString(reader, "Description"),
            CoverPath = GetNullableString(reader, "CoverPath"),
            Duration = TimeSpan.FromSeconds(reader.GetDouble(reader.GetOrdinal("DurationSeconds"))),
            AddedAt = reader.IsDBNull(reader.GetOrdinal("AddedAt")) ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("AddedAt"))),
            AudioFiles = JsonSerializer.Deserialize<List<AudioFile>>(audioFilesJson) ?? new List<AudioFile>(),
            SeriesName = GetNullableString(reader, "SeriesName"),
            SeriesSequence = GetNullableString(reader, "SeriesSequence"),
            Genres = JsonSerializer.Deserialize<List<string>>(genresJson) ?? new List<string>(),
            Tags = JsonSerializer.Deserialize<List<string>>(tagsJson) ?? new List<string>(),
            Chapters = JsonSerializer.Deserialize<List<Chapter>>(chaptersJson) ?? new List<Chapter>(),
            CurrentTime = TimeSpan.FromSeconds(reader.GetDouble(reader.GetOrdinal("CurrentTimeSeconds"))),
            Progress = reader.GetDouble(reader.GetOrdinal("Progress")),
            IsFinished = reader.GetInt32(reader.GetOrdinal("IsFinished")) == 1,
            IsDownloaded = reader.GetInt32(reader.GetOrdinal("IsDownloaded")) == 1,
            LocalPath = GetNullableString(reader, "LocalPath")
        };
    }

    private Library ReadLibrary(SqliteDataReader reader)
    {
        var foldersJson = reader.IsDBNull(reader.GetOrdinal("FoldersJson"))
            ? "[]"
            : reader.GetString(reader.GetOrdinal("FoldersJson"));

        return new Library
        {
            Id = reader.GetString(reader.GetOrdinal("Id")),
            Name = reader.GetString(reader.GetOrdinal("Name")),
            DisplayOrder = reader.GetInt32(reader.GetOrdinal("DisplayOrder")),
            Icon = reader.IsDBNull(reader.GetOrdinal("Icon")) ? "audiobook" : reader.GetString(reader.GetOrdinal("Icon")),
            MediaType = reader.IsDBNull(reader.GetOrdinal("MediaType")) ? "book" : reader.GetString(reader.GetOrdinal("MediaType")),
            Folders = JsonSerializer.Deserialize<List<Folder>>(foldersJson) ?? new List<Folder>()
        };
    }

    private DownloadItem ReadDownloadItem(SqliteDataReader reader)
    {
        var filesToDownloadJson = reader.IsDBNull(reader.GetOrdinal("FilesToDownloadJson"))
            ? "[]"
            : reader.GetString(reader.GetOrdinal("FilesToDownloadJson"));

        return new DownloadItem
        {
            Id = reader.GetString(reader.GetOrdinal("Id")),
            AudioBookId = reader.GetString(reader.GetOrdinal("AudioBookId")),
            Title = reader.GetString(reader.GetOrdinal("Title")),
            Status = (DownloadStatus)reader.GetInt32(reader.GetOrdinal("Status")),
            TotalBytes = reader.GetInt64(reader.GetOrdinal("TotalBytes")),
            DownloadedBytes = reader.GetInt64(reader.GetOrdinal("DownloadedBytes")),
            StartedAt = reader.IsDBNull(reader.GetOrdinal("StartedAt")) ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("StartedAt"))),
            CompletedAt = reader.IsDBNull(reader.GetOrdinal("CompletedAt")) ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("CompletedAt"))),
            ErrorMessage = reader.IsDBNull(reader.GetOrdinal("ErrorMessage")) ? null : reader.GetString(reader.GetOrdinal("ErrorMessage")),
            FilesToDownload = JsonSerializer.Deserialize<List<string>>(filesToDownloadJson) ?? new List<string>()
        };
    }

    private void AddAudioBookParameters(SqliteCommand command, AudioBook audioBook)
    {
        command.Parameters.AddWithValue("@id", audioBook.Id);
        command.Parameters.AddWithValue("@title", audioBook.Title);
        command.Parameters.AddWithValue("@author", audioBook.Author ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@narrator", audioBook.Narrator ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@description", audioBook.Description ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@coverPath", audioBook.CoverPath ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@durationSeconds", audioBook.Duration.TotalSeconds);
        command.Parameters.AddWithValue("@addedAt", audioBook.AddedAt?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@audioFilesJson", JsonSerializer.Serialize(audioBook.AudioFiles));
        command.Parameters.AddWithValue("@currentTimeSeconds", audioBook.CurrentTime.TotalSeconds);
        command.Parameters.AddWithValue("@progress", audioBook.Progress);
        command.Parameters.AddWithValue("@isFinished", audioBook.IsFinished ? 1 : 0);
        command.Parameters.AddWithValue("@isDownloaded", audioBook.IsDownloaded ? 1 : 0);
        command.Parameters.AddWithValue("@localPath", audioBook.LocalPath ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@seriesName", audioBook.SeriesName ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@seriesSequence", audioBook.SeriesSequence ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@genresJson", JsonSerializer.Serialize(audioBook.Genres));
        command.Parameters.AddWithValue("@tagsJson", JsonSerializer.Serialize(audioBook.Tags));
        command.Parameters.AddWithValue("@chaptersJson", JsonSerializer.Serialize(audioBook.Chapters));
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}
