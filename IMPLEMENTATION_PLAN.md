# Nine Lives Audio - UI/UX + Metadata + Efficiency Implementation Plan

## PRIORITY BREAKDOWN

### P0 - CRITICAL (Batch A)
| Item | Complexity | Risk | Description |
|------|------------|------|-------------|
| Central Styles System | M | Low | Unified colors, typography, spacing in ResourceDictionary |
| Page Scaffolding | M | Low | Consistent header + status pill + content area |
| Empty States | S | Low | Friendly messages for empty library/downloads/player |
| Toast/InfoBar System | S | Low | Success/failure notifications |
| Metadata Normalizer | M | Medium | Service to clean author/title/series for display |
| PlaybackSourceResolver | S | Low | Centralize local-vs-stream decision |
| Debounced Search | S | Low | 300ms debounce on search input |
| Cover Image Cache | M | Low | Disk cache with LRU eviction |

### P1 - IMPORTANT (Batch B)
| Item | Complexity | Risk | Description |
|------|------------|------|-------------|
| MiniPlayer Polish | M | Low | Better layout, click-to-navigate, subtle animations |
| BookCard Improvements | M | Low | Clean truncation, download badge, hover states |
| List Virtualization | S | Low | Ensure ItemsRepeater uses virtualization |
| Normalized Metadata in DB | M | Medium | Store computed fields, version for recompute |
| Skeleton Loading | M | Medium | Placeholder rectangles during load |

### P2 - POLISH (Batch C)
| Item | Complexity | Risk | Description |
|------|------------|------|-------------|
| Seamless Stream-to-Local | L | High | Switch source mid-play when download completes |
| Acrylic/Blur Effects | S | Medium | Subtle blur if performant |
| Timing Diagnostics | S | Low | Log startup/fetch/render times in diag mode |

---

## BATCH A IMPLEMENTATION

### 1. Central Styles System (Styles.xaml rewrite)

**Changes:**
- Define color palette (semantic names)
- Define spacing scale (4/8/12/16/24/32)
- Define typography styles (Title, Subtitle, Body, Caption, Mono)
- Define common corner radius, shadows, thicknesses
- Remove duplicated inline styles from pages

**Files:** `Resources/Styles.xaml`

### 2. Page Scaffolding

**Changes:**
- Create reusable `PageHeader` style with:
  - Title text (SubtitleTextBlockStyle)
  - Status pill (Connected/Offline/Syncing)
  - Consistent padding
- Apply to all pages: Library, Player, Downloads, Settings

**Files:** `Resources/Styles.xaml`, all `Views/*.xaml`

### 3. Empty States

**Changes:**
- Create `EmptyStateTemplate` DataTemplate:
  - Icon (FontIcon)
  - Title text
  - Subtitle/action text
  - Optional action button
- Apply to:
  - LibraryPage: "No books found. Connect to server or refresh."
  - DownloadsPage: "No downloads yet. Download a book to listen offline."
  - PlayerPage: "Nothing playing. Pick a book from your library."

**Files:** `Resources/Styles.xaml`, `Views/*.xaml`

### 4. Toast/InfoBar System

**Changes:**
- Add `InfoBar` to MainWindow (top or bottom)
- Create `INotificationService` for showing toasts
- Integrate with download events, connection status, errors

**Files:** `Services/NotificationService.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`

### 5. Metadata Normalizer

**Service:** `IMetadataNormalizer`
```csharp
public interface IMetadataNormalizer
{
    NormalizedMetadata Normalize(AudioBook raw);
}

public class NormalizedMetadata
{
    public string DisplayTitle { get; set; }
    public string DisplayAuthor { get; set; }
    public string DisplaySeries { get; set; }
    public string DisplayNarrator { get; set; }
    public string RawTitle { get; set; }
    public double? SeriesNumber { get; set; }
    public List<string> AuthorList { get; set; }
}
```

**Normalization Rules:**
1. Author: "Last, First" → "First Last", split on ";/&", dedupe
2. Title: Remove "[Unabridged]", "(Audiobook)", etc.
3. Series: Extract from title if missing (e.g., "Series Name #3")
4. Narrator: Remove "Read by", "Narrated by" prefixes

**Files:** `Services/MetadataNormalizer.cs`, `Services/IMetadataNormalizer.cs`

### 6. PlaybackSourceResolver

**Changes:**
- Extract source decision logic from AudioPlaybackService
- Create `PlaybackSourceResolver` that returns:
  - `LocalFile` path if downloaded and valid
  - `StreamUrl` if authenticated
  - `Error` if neither available
- AudioPlaybackService calls resolver, acts on result

**Files:** `Services/PlaybackSourceResolver.cs`

### 7. Debounced Search

**Changes:**
- Add `DispatcherTimer` debounce in LibraryViewModel
- Only call `ApplyFilter()` after 300ms of no typing
- Cancel pending filter on new input

**Files:** `ViewModels/LibraryViewModel.cs`

### 8. Cover Image Cache

**Changes:**
- Create `ICoverCacheService`:
  - `GetCoverAsync(itemId, coverUrl)` → local path or null
  - Stores in `%LOCALAPPDATA%/NineLivesAudio/Covers/`
  - LRU eviction when cache exceeds 100MB
- Update image bindings to use cached path

**Files:** `Services/CoverCacheService.cs`, `Helpers/CoverImageConverter.cs`

---

## BATCH B IMPLEMENTATION

### 1. MiniPlayer Polish
- Larger touch targets
- Click anywhere (except controls) navigates to PlayerPage
- Subtle scale animation on play/pause button

### 2. BookCard Improvements
- Download status badge with icon
- Progress bar only when > 0%
- Truncation with ellipsis (2-line title, 1-line author)

### 3. List Virtualization
- Verify ItemsRepeater uses proper recycling
- Set ItemTemplate explicitly to avoid re-creation

### 4. Normalized Metadata in DB
- Add `normalized_version` column to AudioBooks
- Store computed fields (DisplayTitle, DisplayAuthor, etc.)
- Recompute when version mismatch

### 5. Skeleton Loading
- Create `SkeletonBookCard` with gray rectangles
- Show during initial load, replace with real cards

---

## BATCH C IMPLEMENTATION

### 1. Seamless Stream-to-Local Switch
- On DownloadCompleted, if currently playing same book:
  - Save current position
  - Stop MediaPlayer
  - Switch to NAudio with local file
  - Seek to saved position
  - Resume playback

### 2. Acrylic/Blur
- Test AcrylicBrush on MiniPlayer background
- Disable if GPU acceleration unavailable

### 3. Timing Diagnostics
- Add Stopwatch around key operations
- Log only in DiagnosticsMode

---

## TEST CHECKLIST

### Batch A Tests
- [ ] App launches without crash
- [ ] All pages have consistent header with title
- [ ] Status pill shows correct state (Connected/Offline)
- [ ] Empty library shows friendly message with Refresh button
- [ ] Empty downloads shows "Download a book" message
- [ ] Player page with no book shows "Pick a book" message
- [ ] Download complete shows toast notification
- [ ] Connection error shows error toast
- [ ] Author "Sanderson, Brandon" displays as "Brandon Sanderson"
- [ ] Title "Mistborn (Unabridged)" displays as "Mistborn"
- [ ] Downloaded book plays from local file (no network call)
- [ ] Search is debounced (type fast, filter only fires once)
- [ ] Cover images load from cache on repeat views

### Batch B Tests
- [ ] MiniPlayer click navigates to PlayerPage
- [ ] Play/pause button has subtle animation
- [ ] BookCard shows download badge when downloaded
- [ ] Long titles truncate with ellipsis
- [ ] Large library (100+ books) scrolls smoothly
- [ ] Initial load shows skeleton placeholders

### Batch C Tests
- [ ] Playing stream, download completes, switches to local seamlessly
- [ ] Acrylic effect visible on MiniPlayer (if enabled)
- [ ] Diagnostics mode shows timing logs

---

## EFFICIENCY GAINS

1. **Debounced search**: Reduces filter calls from N to 1 per search session
2. **Cover cache**: Eliminates repeated network requests for same image
3. **Throttled position updates**: Already at 4/sec, confirmed in code
4. **Metadata normalization once**: Compute on ingest, not every render
5. **List virtualization**: Only renders visible items (already in place with ItemsRepeater)
6. **Local playback preference**: Eliminates session API call + streaming overhead

---

## RISKS & MITIGATIONS

| Risk | Mitigation |
|------|------------|
| Metadata regex breaks edge cases | Keep RawTitle/RawAuthor for fallback |
| Cover cache fills disk | LRU eviction at 100MB limit |
| Acrylic causes GPU issues | Check for hardware acceleration, disable if not available |
| Stream-to-local switch causes audio glitch | Save position with 500ms buffer, test extensively |

