namespace RpgSceneMaker.Api.Models;

/// <summary>A named table state: lighting + music + one-shot sound effects.</summary>
public class Scene
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public LightSettings? Light { get; set; }
    public MusicSettings? Music { get; set; }

    /// <summary>Kenku soundboard sound ids fired when the scene activates.</summary>
    public List<string> SoundEffects { get; set; } = [];
}

public class LightSettings
{
    public bool? Power { get; set; }

    /// <summary>Hex color like "#FF8C2A". When set, the bulb switches to colour mode.</summary>
    public string? Color { get; set; }

    /// <summary>0-100.</summary>
    public int? Brightness { get; set; }

    /// <summary>White color temperature, 0 (warm) - 100 (cold). When set (and no Color), the bulb switches to white mode.</summary>
    public int? Temperature { get; set; }
}

public class MusicSettings
{
    /// <summary>Kenku playlist or track id to play. Leave null to keep current music.</summary>
    public string? PlayId { get; set; }

    /// <summary>0.0 - 1.0.</summary>
    public double? Volume { get; set; }

    /// <summary>Pause whatever is playing instead of starting something.</summary>
    public bool Pause { get; set; }
}
