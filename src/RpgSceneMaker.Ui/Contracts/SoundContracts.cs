namespace RpgSceneMaker.Ui.Contracts;

// Mirrors the API's sound contracts (contracts are duplicated per project by design — keep in sync by hand).
// Image is an optional full-art tile background. DurationMs is the file's natural length in ms; the
// server uses null (not yet measured) and 0 (tried, couldn't measure) as "unknown" sentinels — read it
// through NaturalMs, which folds both to null. Waveform is a compact amplitude preview (peaks 0–255,
// base64 over the wire) drawn on timeline sound clips; null/empty when not measured or undecodable.
public record SoundDto(string Id, string Name, string Category, double Volume, bool Loop, string? Image = null, int? DurationMs = null, byte[]? Waveform = null)
{
    // The file's natural length in ms, or null when unknown (server sentinel: null or <= 0).
    public int? NaturalMs => DurationMs is { } d && d > 0 ? d : null;

    // Waveform peaks (0–255) when measured and decodable, else null (never measured or empty sentinel).
    public byte[]? Peaks => Waveform is { Length: > 0 } w ? w : null;
}
public record SoundStateDto(List<string> Playing);

// Mutable form model for editing one sound in the panel.
public class SoundEdit
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public int VolumePercent { get; set; } = 100;
    public bool Loop { get; set; }
    public string? Image { get; set; }
}
