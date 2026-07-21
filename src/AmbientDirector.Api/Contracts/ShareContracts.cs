namespace AmbientDirector.Api.Contracts;

// Wire DTOs for the share import flow. The panel mirrors these by hand in
// AmbientDirector.Ui/Contracts/ShareContracts.cs — keep the two in sync.

/// <summary>What a just-uploaded pack contains, returned by <c>POST /share/import/inspect</c> without committing
/// anything. The <see cref="TempId"/> is handed back to <c>commit</c> to apply the import.</summary>
public sealed record ShareInspectResult(
    string TempId,
    string PrimaryKind,
    string PrimaryId,
    Dictionary<string, int> Counts,
    List<ShareCollision> Collisions,
    List<ShareLightKeyDto> LightKeys,
    int MediaCount,
    List<string> MediaMissing);

/// <summary>One bundled entity whose id already exists locally — on the default <c>copy</c> policy it will be
/// imported under a fresh id.</summary>
public sealed record ShareCollision(string Kind, string Id, string Name);

/// <summary>A distinct source light key in the pack, with the owner labels that used it, for the remap UI.</summary>
public sealed record ShareLightKeyDto(string Key, List<string> Sources);

/// <summary>The commit request: the temp id from inspect, the chosen light-key mapping (source key → target
/// registered light key, or null/empty/"skip" to drop that binding), and an optional collision policy
/// (<c>copy</c> (default) / <c>overwrite</c> / <c>skip</c>).</summary>
public sealed record ShareCommitInput(
    string TempId,
    Dictionary<string, string?>? LightKeys,
    string? CollisionPolicy);

/// <summary>The result of a commit: created ids per kind, how many media files were recreated, and the
/// ids that had to be changed to avoid a collision (so the UI can say "imported as 'tavern-2'").</summary>
public sealed record ShareCommitResult(
    Dictionary<string, List<string>> Created,
    int MediaImported,
    List<ShareRemap> Remapped);

public sealed record ShareRemap(string Kind, string OldId, string NewId);
