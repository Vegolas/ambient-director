namespace RpgSceneMaker.Api.Contracts;

/// <summary>Wire shape for a sound-effect library entry.</summary>
public record SoundDto(string Id, string Name, string Category, double Volume, bool Loop, string? Image);

/// <summary>Editable fields for a sound; each null field is left unchanged (partial update), except
/// <see cref="Image"/> which is set as sent (null clears the tile background).</summary>
public record SoundUpdateInput(string? Name, string? Category, double? Volume, bool? Loop, string? Image);

/// <summary>Ids of the sounds currently playing on the server, for the panel's live highlight.</summary>
public record SoundStateDto(IReadOnlyList<string> Playing);
