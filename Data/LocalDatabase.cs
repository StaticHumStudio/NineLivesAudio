using NineLivesAudio.Models;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text.Json;
using T = NineLivesAudio.Data.Db.Tables;
using PP = NineLivesAudio.Data.Db.Progress;
using PPU = NineLivesAudio.Data.Db.PendingProgress;

namespace NineLivesAudio.Data;

public class LocalDatabase : ILocalDatabase, IDisposable
{
    private readonly string _dbPath;
    private SqliteConnection? _connection;
    private bool _initialized;

    public LocalDatabase()
    {
        var localFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(localFolder, "NineLivesAudio");
        Directory.CreateDirectory(appFolder);
        _dbPath = Path.Combine(appFolder, "audiobookshelf.db");
    }

    /// <summary>
    /// Constructor for testing — accepts a pre-opened connection.
    /// </summary>
    internal LocalDatabase(SqliteConnection connection)
    {
        _dbPath = ":memory:";
        _connection = connection;
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;

        if (_connection == null)
        {
            _connection = new SqliteConnection($"Data Source={_dbPath}");
            await _connection.OpenAsync();
        }

        await CreateTablesAsync();
        await MigrateAsync();
        _initialized = true;
    }

    private async Task MigrateAsync()
    {
        // Add new columns if they don't exist (for existing databases)
        var columnsToAdd = new[]
        {
            (T.AudioBooks, Db.AudioBook.SeriesName, "TEXT"),
            (T.AudioBooks, Db.AudioBook.SeriesSequence, "TEXT"),
            (T.AudioBooks, Db.AudioBook.GenresJson, "TEXT"),
            (T.AudioBooks, Db.AudioBook.TagsJson, "TEXT"),
            (T.AudioBooks, Db.AudioBook.ChaptersJson, "TEXT"),
        };

        foreach (var (table, column, type) in columnsToAdd)
        {
            if (await ColumnExistsAsync(table, column))
                continue;

            try
            {
                var cmd = _connection!.CreateCommand();
                // Table/column names come from compile-time constants above, not user input
                cmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type}";
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqliteException ex)
            {
                // Log actual failures instead of silently swallowing
                System.Diagnostics.Debug.WriteLine($"[DB Migration] Failed to add {table}.{column}: {ex.Message}");
            }
        }
    }

    private async Task<bool> ColumnExistsAsync(string table, string column)
    {
        var cmd = _connection!.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
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

    // ── AudioBooks ──────────────────────────────────────────────────────

    public async Task<List<AudioBook>> GetAllAudioBooksAsync()
    {
        EnsureInitialized();
        var audioBooks = new List<AudioBook>();

        var command = _connection!.CreateCommand();
        command.CommandText = $"SELECT * FROM {T.AudioBooks} ORDER BY Title";

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            audioBooks.Add(AudioBookMapper.Map((SqliteDataReader)reader));
        }

        return audioBooks;
    }

    public async Task<List<AudioBook>> GetAudioBooksByLibraryAsync(string libraryId)
    {
        EnsureInitialized();
        var audioBooks = new List<AudioBook>();

        var command = _connection!.CreateCommand();
        command.CommandText = "SELECT * FROM AudioBooks WHERE LibraryId = @libraryId ORDER BY Title";
        command.Parameters.AddWithValue("@libraryId", libraryId);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            audioBooks.Add(AudioBookMapper.Map((SqliteDataReader)reader));
        }

        return audioBooks;
    }

    public async Task<AudioBook?> GetAudioBookAsync(string id)
    {
        EnsureInitialized();

        var command = _connection!.CreateCommand();
        command.CommandText = $"SELECT * FROM {T.AudioBooks} WHERE Id = @id";
        command.Parameters.AddWithValue("@id", id);

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return AudioBookMapper.Map((SqliteDataReader)reader);
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

        AudioBookMapper.AddParameters(command, audioBook);
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
        command.CommandText = $"DELETE FROM {T.AudioBooks} WHERE Id = @id";
        command.Parameters.AddWithValue("@id", id);
        await command.ExecuteNonQueryAsync();
    }

    // ── Libraries ───────────────────────────────────────────────────────

    public async Task<List<Library>> GetAllLibrariesAsync()
    {
        EnsureInitialized();
        var libraries = new List<Library>();

        var command = _connection!.CreateCommand();
        command.CommandText = $"SELECT * FROM {T.Libraries} ORDER BY DisplayOrder";

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            libraries.Add(LibraryMapper.Map((SqliteDataReader)reader));
        }

        return libraries;
    }

    public async Task<Library?> GetLibraryAsync(string id)
    {
        EnsureInitialized();

        var command = _connection!.CreateCommand();
        command.CommandText = $"SELECT * FROM {T.Libraries} WHERE Id = @id";
        command.Parameters.AddWithValue("@id", id);

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return LibraryMapper.Map((SqliteDataReader)reader);
        }

        return null;
    }

    public async Task SaveLibraryAsync(Library library)
    {
        EnsureInitialized();

        var command = _connection!.CreateCommand();
        command.CommandText = $@"
            INSERT OR REPLACE INTO {T.Libraries} (Id, Name, DisplayOrder, Icon, MediaType, FoldersJson)
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
        command.CommandText = $"DELETE FROM {T.Libraries} WHERE Id = @id";
        command.Parameters.AddWithValue("@id", id);
        await command.ExecuteNonQueryAsync();
    }

    // ── Downloads ────────────────────────────────────────────────────────

    public async Task<List<DownloadItem>> GetAllDownloadItemsAsync()
    {
        EnsureInitialized();
        var downloads = new List<DownloadItem>();

        var command = _connection!.CreateCommand();
        command.CommandText = $"SELECT * FROM {T.DownloadItems} ORDER BY StartedAt DESC";

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            downloads.Add(DownloadItemMapper.Map((SqliteDataReader)reader));
        }

        return downloads;
    }

    public async Task<DownloadItem?> GetDownloadItemAsync(string id)
    {
        EnsureInitialized();

        var command = _connection!.CreateCommand();
        command.CommandText = $"SELECT * FROM {T.DownloadItems} WHERE Id = @id";
        command.Parameters.AddWithValue("@id", id);

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return DownloadItemMapper.Map((SqliteDataReader)reader);
        }

        return null;
    }

    public async Task SaveDownloadItemAsync(DownloadItem downloadItem)
    {
        EnsureInitialized();

        var command = _connection!.CreateCommand();
        command.CommandText = $@"
            INSERT OR REPLACE INTO {T.DownloadItems}
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
        command.CommandText = $"DELETE FROM {T.DownloadItems} WHERE Id = @id";
        command.Parameters.AddWithValue("@id", id);
        await command.ExecuteNonQueryAsync();
    }

    // ── Playback Progress ───────────────────────────────────────────────

    public async Task SavePlaybackProgressAsync(string audioBookId, TimeSpan position, bool isFinished, DateTime? updatedAt = null)
    {
        EnsureInitialized();

        var command = _connection!.CreateCommand();
        command.CommandText = $@"
            INSERT OR REPLACE INTO {T.PlaybackProgress} ({PP.AudioBookId}, {PP.PositionSeconds}, {PP.IsFinished}, {PP.UpdatedAt})
            VALUES (@audioBookId, @positionSeconds, @isFinished, @updatedAt)";

        command.Parameters.AddWithValue("@audioBookId", audioBookId);
        command.Parameters.AddWithValue("@positionSeconds", position.TotalSeconds);
        command.Parameters.AddWithValue("@isFinished", isFinished ? 1 : 0);
        command.Parameters.AddWithValue("@updatedAt", (updatedAt ?? DateTime.UtcNow).ToString("O"));

        await command.ExecuteNonQueryAsync();
    }

    public async Task<(TimeSpan position, bool isFinished)?> GetPlaybackProgressAsync(string audioBookId)
    {
        EnsureInitialized();

        var command = _connection!.CreateCommand();
        command.CommandText = $"SELECT {PP.PositionSeconds}, {PP.IsFinished} FROM {T.PlaybackProgress} WHERE {PP.AudioBookId} = @audioBookId";
        command.Parameters.AddWithValue("@audioBookId", audioBookId);

        using var reader = (SqliteDataReader)await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var positionSeconds = reader.GetDoubleOrDefault(PP.PositionSeconds);
            var finished = reader.GetBoolFromInt(PP.IsFinished);
            return (TimeSpan.FromSeconds(positionSeconds), finished);
        }

        return null;
    }

    public async Task<(TimeSpan position, bool isFinished, DateTime updatedAt)?> GetPlaybackProgressWithTimestampAsync(string audioBookId)
    {
        EnsureInitialized();

        var command = _connection!.CreateCommand();
        command.CommandText = $"SELECT {PP.PositionSeconds}, {PP.IsFinished}, {PP.UpdatedAt} FROM {T.PlaybackProgress} WHERE {PP.AudioBookId} = @audioBookId";
        command.Parameters.AddWithValue("@audioBookId", audioBookId);

        using var reader = (SqliteDataReader)await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var positionSeconds = reader.GetDoubleOrDefault(PP.PositionSeconds);
            var finished = reader.GetBoolFromInt(PP.IsFinished);
            var updatedAtDt = reader.GetNullableDateTime(PP.UpdatedAt) ?? DateTime.MinValue;
            return (TimeSpan.FromSeconds(positionSeconds), finished, updatedAtDt);
        }

        return null;
    }

    // ── Offline Progress Queue ──────────────────────────────────────────

    public async Task EnqueuePendingProgressAsync(string itemId, double currentTime, bool isFinished)
    {
        EnsureInitialized();
        var command = _connection!.CreateCommand();
        command.CommandText = $@"
            INSERT INTO {T.PendingProgressUpdates} ({PPU.ItemId}, {PPU.CurrentTime}, {PPU.IsFinished}, {PPU.Timestamp})
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
        command.CommandText = $"SELECT {PPU.ItemId}, {PPU.CurrentTime}, {PPU.IsFinished}, {PPU.Timestamp} FROM {T.PendingProgressUpdates} ORDER BY {PPU.Timestamp} ASC";
        using var reader = (SqliteDataReader)await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            entries.Add(new PendingProgressEntry
            {
                ItemId = reader.GetString(reader.GetOrdinal(PPU.ItemId)),
                CurrentTime = reader.GetDoubleOrDefault(PPU.CurrentTime),
                IsFinished = reader.GetBoolFromInt(PPU.IsFinished),
                Timestamp = reader.GetNullableDateTime(PPU.Timestamp) ?? DateTime.MinValue
            });
        }
        return entries;
    }

    public async Task ClearPendingProgressAsync()
    {
        EnsureInitialized();
        var command = _connection!.CreateCommand();
        command.CommandText = $"DELETE FROM {T.PendingProgressUpdates}";
        await command.ExecuteNonQueryAsync();
    }

    public async Task ClearPendingProgressForItemsAsync(IEnumerable<string> itemIds)
    {
        EnsureInitialized();
        var itemIdsList = itemIds.ToList();
        if (!itemIdsList.Any()) return;

        var command = _connection!.CreateCommand();
        var placeholders = string.Join(",", itemIdsList.Select((_, i) => $"@id{i}"));
        command.CommandText = $"DELETE FROM {T.PendingProgressUpdates} WHERE {PPU.ItemId} IN ({placeholders})";
        for (int i = 0; i < itemIdsList.Count; i++)
        {
            command.Parameters.AddWithValue($"@id{i}", itemIdsList[i]);
        }
        await command.ExecuteNonQueryAsync();
    }

    public async Task<int> GetPendingProgressCountAsync()
    {
        EnsureInitialized();
        var command = _connection!.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {T.PendingProgressUpdates}";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    // ── Nine Lives (recently played) ────────────────────────────────────

    public async Task<List<(AudioBook Book, DateTime LastPlayedAt)>> GetRecentlyPlayedAsync(int limit = 9)
    {
        EnsureInitialized();
        var results = new List<(AudioBook Book, DateTime LastPlayedAt)>();

        var command = _connection!.CreateCommand();
        command.CommandText = $@"
            SELECT ab.*, pp.{PP.UpdatedAt} AS LastPlayedAt FROM {T.AudioBooks} ab
            INNER JOIN {T.PlaybackProgress} pp ON ab.Id = pp.{PP.AudioBookId}
            ORDER BY pp.{PP.UpdatedAt} DESC
            LIMIT @limit";
        command.Parameters.AddWithValue("@limit", limit);

        using var reader = (SqliteDataReader)await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var book = AudioBookMapper.Map(reader);
            var lastPlayed = reader.GetNullableDateTime("LastPlayedAt") ?? DateTime.MinValue;
            results.Add((book, lastPlayed));
        }

        return results;
    }

    // ── Bulk operations ─────────────────────────────────────────────────

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
        command.CommandText = $@"
            DELETE FROM {T.AudioBooks};
            DELETE FROM {T.Libraries};
            DELETE FROM {T.DownloadItems};
            DELETE FROM {T.PlaybackProgress};
        ";
        await command.ExecuteNonQueryAsync();
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}
