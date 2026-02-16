namespace NineLivesAudio.Models;

/// <summary>
/// View model for displaying a chapter in the book detail/player chapter list.
/// Contains pre-formatted strings and computed display properties.
/// </summary>
public class ChapterDisplayItem
{
    public int Index { get; set; }
    public string Title { get; set; } = string.Empty;
    public string StartTime { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
    public bool IsCurrent { get; set; }
    public double ProgressPercent { get; set; }
    public string Status { get; set; } = "upcoming"; // "done", "current", "upcoming"

    public string StatusIcon => Status switch
    {
        "done" => "\u2713",     // checkmark
        "current" => "\u25B6",  // play triangle
        _ => ""
    };

    public string TitleWeight => IsCurrent ? "SemiBold" : "Normal";
    public string ChapterBackground => IsCurrent ? "#1AC5A55A" : "Transparent";
    public string ProgressBarVisibility => IsCurrent ? "Visible" : "Collapsed";
}
