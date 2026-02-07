using System;
using System.Collections.Generic;

namespace AudioBookshelfApp.Models;

public class AudioBook
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string? Narrator { get; set; }
    public string? Description { get; set; }
    public string? CoverPath { get; set; }
    public TimeSpan Duration { get; set; }
    public DateTime? AddedAt { get; set; }
    public List<AudioFile> AudioFiles { get; set; } = new();

    // Series information
    public string? SeriesName { get; set; }
    public string? SeriesSequence { get; set; }

    // Collections/Tags
    public List<string> Genres { get; set; } = new();
    public List<string> Tags { get; set; } = new();

    // Chapters
    public List<Chapter> Chapters { get; set; } = new();

    public TimeSpan CurrentTime { get; set; }
    public double Progress { get; set; }
    public bool IsFinished { get; set; }

    /// <summary>Progress as 0–100 regardless of whether the API returned 0–1 or 0–100.</summary>
    public double ProgressPercent => Progress <= 1.0 ? Progress * 100.0 : Progress;

    /// <summary>Whether this book has any progress to display.</summary>
    public bool HasProgress => Progress > 0;

    /// <summary>Formatted progress text for library tiles — shows chapter info if available.</summary>
    public string ProgressText
    {
        get
        {
            if (Progress <= 0) return string.Empty;
            var bookPct = $"{ProgressPercent:F0}%";
            if (Chapters.Count > 0)
            {
                var idx = GetCurrentChapterIndex();
                if (idx >= 0)
                    return $"{bookPct} \u2022 Ch {idx + 1}/{Chapters.Count}";
            }
            return bookPct;
        }
    }

    /// <summary>Find the current chapter based on CurrentTime.</summary>
    public Chapter? GetCurrentChapter()
    {
        if (Chapters.Count == 0) return null;
        var posSeconds = CurrentTime.TotalSeconds;
        return Chapters.FirstOrDefault(c => posSeconds >= c.Start && posSeconds < c.End)
               ?? (posSeconds >= Chapters.Last().End ? Chapters.Last() : null);
    }

    /// <summary>Get current chapter index (0-based), or -1 if none.</summary>
    public int GetCurrentChapterIndex()
    {
        if (Chapters.Count == 0) return -1;
        var posSeconds = CurrentTime.TotalSeconds;
        for (int i = 0; i < Chapters.Count; i++)
        {
            if (posSeconds >= Chapters[i].Start && posSeconds < Chapters[i].End)
                return i;
        }
        return posSeconds >= Chapters.Last().End ? Chapters.Count - 1 : -1;
    }

    /// <summary>Progress within the current chapter as 0-100.</summary>
    public double CurrentChapterProgressPercent
    {
        get
        {
            var ch = GetCurrentChapter();
            if (ch == null || ch.Duration.TotalSeconds <= 0) return 0;
            var elapsed = CurrentTime.TotalSeconds - ch.Start;
            return Math.Clamp(elapsed / ch.Duration.TotalSeconds * 100.0, 0, 100);
        }
    }

    /// <summary>Formatted chapter progress text (e.g. "Ch 5/71 - 42%").</summary>
    public string ChapterProgressText
    {
        get
        {
            if (Chapters.Count == 0 || !HasProgress) return string.Empty;
            var idx = GetCurrentChapterIndex();
            if (idx < 0) return string.Empty;
            var pct = (int)CurrentChapterProgressPercent;
            return $"Ch {idx + 1}/{Chapters.Count} \u2022 {pct}%";
        }
    }

    public bool IsDownloaded { get; set; }
    public string? LocalPath { get; set; }
}

public class AudioFile
{
    public string Id { get; set; } = string.Empty;
    public string Ino { get; set; } = string.Empty;  // AudioBookshelf internal reference
    public int Index { get; set; }
    public TimeSpan Duration { get; set; }
    public string Filename { get; set; } = string.Empty;
    public string? LocalPath { get; set; }
    public string? MimeType { get; set; }
    public long Size { get; set; }
}

public class Chapter
{
    public int Id { get; set; }
    public double Start { get; set; }
    public double End { get; set; }
    public string Title { get; set; } = string.Empty;

    public TimeSpan StartTime => TimeSpan.FromSeconds(Start);
    public TimeSpan EndTime => TimeSpan.FromSeconds(End);
    public TimeSpan Duration => TimeSpan.FromSeconds(End - Start);
}
