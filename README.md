# Nine Lives Audio

A Windows desktop client for [Audiobookshelf](https://www.audiobookshelf.org/) built with WinUI 3, .NET 10, and Windows App SDK 1.6. Browse your library, stream or play downloaded audiobooks, track progress across devices, and pick up right where you left off.

> **Note:** This app is not affiliated with or endorsed by the Audiobookshelf project. Audiobookshelf is open-source software licensed under GPL-3.0.

## Features

### Nine Lives (Home Page)
- Shows your 9 most recently played audiobooks in an adaptive grid
- Cover art, title, author, and progress at a glance
- Tap any card to jump straight to book details
- Automatically refreshes after library sync

### Library
- Browse all books from your Audiobookshelf server
- Grid and list view modes with search and filtering
- Multi-library support
- "Downloaded" badge on books available offline
- Quick-play overlay on hover
- Connection status indicator (Connected / Offline)
- Auto-fallback to downloaded-only mode when server is unreachable

### Book Details
- Full metadata: title, author, narrator, series, duration, description
- Genres, tags, and chapter listing
- Visual progress bar with percentage and chapter-level tracking
- Play / Resume and Download action buttons
- Clickable author and series links to filter library
- "Downloaded" badge for offline-ready books
- Fresh DB reload before playback ensures local files are used when available

### Player
- **Dual-mode playback** — local files via NAudio, streaming via Windows MediaPlayer
- **Multi-track support** — seamless auto-advance across multi-file audiobooks
- **Chapter navigation** — jump to any chapter, previous/next chapter buttons
- **Chapter mode toggle** — switch progress bar between book-level and chapter-level
- **Playback speed** — 0.5x to 3.0x
- **Sleep timer** — preset durations from 5 minutes to 2 hours with countdown display
- **Volume control** with mute detection
- **Bookmarks** — create, name, jump to, and delete bookmarks at any position
- **Source badge** — shows "Playing from local file" or "Streaming from server"
- **Pop-out mini player** — compact floating window with transport controls, progress slider, and always-on-top toggle
- **SMTC integration** — Windows lock screen and media overlay controls
- **Back navigation** — responsive back button on all pages (icon-only in narrow mode)

### Pop-Out Mini Player
- Compact 360x200 floating window with transport controls (skip back 10s, play/pause, skip forward 30s)
- Progress slider with current time and remaining time labels
- Always-on-top toggle pin to keep the player above other windows
- Expand button to return to the full app
- Auto-closes when playback stops
- Dark themed title bar matching the main app
- Singleton lifecycle — only one mini player open at a time

### Downloads
- Queue-based downloads with 2 concurrent slots
- Logical folder structure: `Music/AudioBookshelf/Author - Title/`
- Real-time progress bar with percentage and byte count
- Pause, resume, and cancel individual downloads
- Automatic retry with exponential backoff (up to 3 attempts)
- Atomic file operations (`.part` file renamed on completion)
- File size display on completed downloads
- Delete downloads from the app to reclaim disk space
- Orphaned `.part` file cleanup on startup
- Optional automatic cover art download

### Sync & Offline
- **Library sync** — pulls libraries and book metadata from the server on a configurable interval
- **Progress sync** — fetches user progress from the server and seeds local database
- **Session sync** — 12-second heartbeat pushes playback position to the server during active listening
- **Offline progress queue** — progress updates are queued locally when the server is unreachable and drained automatically when connectivity returns
- **Download state preservation** — sync preserves local AudioFiles and download paths even when the server returns incomplete metadata
- **Disk recovery** — automatically scans download directories and reconstructs AudioFile metadata from files on disk when server data is missing
- **Tiered file matching** — matches audio files across sync by inode, filename, then index (resilient to server metadata changes)
- **Manual sync** — trigger a full sync from the Settings page at any time

### Settings
- Server URL, username, and password configuration
- Theme selection (System, Light, Dark)
- Default playback speed and volume
- Sync interval (1 to 30 minutes)
- Custom download path
- Auto-download covers toggle
- Start minimized / minimize to tray options
- Self-signed certificate support
- Manual "Sync Now" button for on-demand library and progress sync
- Diagnostics mode with logging (exports to AppData, not Desktop)

### Responsive Layout
- Three adaptive breakpoints: Narrow (<600px), Medium (600-900px), Wide (>900px)
- Stacked vs. side-by-side layouts that adapt to window size
- Mini player bar for persistent playback controls while browsing
- Pop-out mini player window for always-on-top compact controls
- Back button text collapses to icon-only in narrow mode

## Tech Stack

| Component | Technology |
|-----------|-----------|
| UI Framework | WinUI 3 / Windows App SDK 1.6 |
| Runtime | .NET 10 |
| Architecture | MVVM (CommunityToolkit.Mvvm 8.4) |
| Local Audio | NAudio 2.2 |
| Streaming Audio | Windows.Media.Playback.MediaPlayer |
| Database | SQLite (Microsoft.Data.Sqlite 10.0) |
| DI | Microsoft.Extensions.DependencyInjection 10.0 |
| Target OS | Windows 10 (1809+) / Windows 11 |

## Building

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Windows 10 SDK (10.0.22621.0)
- Windows App SDK 1.6

### Build
```bash
dotnet build -r win-x64
```

### Run
```bash
dotnet run -r win-x64
```

> **Note:** 65 MVVMTK0045 warnings are expected (pre-existing, non-blocking). These are AOT compatibility advisories from CommunityToolkit.Mvvm.

## Project Structure

```
NineLivesAudio/
  App.xaml(.cs)              # Application entry point and DI container
  MainWindow.xaml(.cs)       # Shell window with navigation frame
  Controls/
    BookCard.xaml(.cs)       # Reusable book cover card with hover overlay
    MiniPlayer.xaml(.cs)     # Compact playback bar (bottom of window)
  Data/
    LocalDatabase.cs         # SQLite persistence (books, progress, downloads)
    ILocalDatabase.cs        # Database interface
  Helpers/
    Converters.cs            # XAML value converters
  Models/
    AudioBook.cs             # Book model with chapters, audio files, progress
    DownloadItem.cs          # Download queue item with status and progress
    AppSettings.cs           # User preferences
    Library.cs               # Server library model
    Bookmark.cs              # Bookmark with timestamp
    ServerProfile.cs         # Server connection profile
    UserProgress.cs          # Progress sync DTO
  Resources/
    Styles.xaml              # Global styles and design tokens
    Converters.xaml          # XAML converter resources
  Services/
    AudioPlaybackService.cs  # NAudio + MediaPlayer dual-mode playback
    AudioBookshelfApiService.cs  # Audiobookshelf REST API client
    DownloadService.cs       # Queue-based file download engine
    SyncService.cs           # Library and progress synchronization
    PlaybackSourceResolver.cs    # Decides local vs. streaming playback
    SettingsService.cs       # Persistent app settings
    ConnectivityService.cs   # Network monitoring
    NavigationService.cs     # Frame navigation
    NotificationService.cs   # In-app toast notifications
    MetadataNormalizer.cs    # Title/author display formatting
    LoggingService.cs        # Diagnostic logging
    OfflineProgressQueue.cs  # Queued progress for offline sync
    AppInitializer.cs        # Startup orchestration
  ViewModels/
    HomeViewModel.cs         # Nine Lives data
    LibraryViewModel.cs      # Library browsing and filtering
    PlayerViewModel.cs       # Playback state and commands
    DownloadsViewModel.cs    # Download queue management
    MainViewModel.cs         # Shell and mini player state
    SettingsViewModel.cs     # Settings page logic
  Views/
    HomePage.xaml(.cs)       # Nine Lives recently played
    LibraryPage.xaml(.cs)    # Book browsing grid/list
    BookDetailPage.xaml(.cs) # Book info, chapters, actions
    PlayerPage.xaml(.cs)     # Full player with controls
    MiniPlayerWindow.xaml(.cs)  # Pop-out compact player window
    DownloadsPage.xaml(.cs)  # Download queue and completed list
    SettingsPage.xaml(.cs)   # App configuration
```

## Server Compatibility

Designed for [Audiobookshelf](https://www.audiobookshelf.org/) servers. Tested against Audiobookshelf API v1. Requires a running server instance with a valid user account.

## License

Private project.
