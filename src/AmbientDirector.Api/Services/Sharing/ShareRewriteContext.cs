namespace AmbientDirector.Api.Services.Sharing;

/// <summary>Case-insensitive equality for a (kind, id) pair — ids are NOCASE and kinds are lowercase slugs, so
/// the export closure's visited set and the import id-map compare both parts case-insensitively.</summary>
public sealed class ShareKeyComparer : IEqualityComparer<(string Kind, string Id)>
{
    public static readonly ShareKeyComparer Instance = new();

    public bool Equals((string Kind, string Id) x, (string Kind, string Id) y) =>
        string.Equals(x.Kind, y.Kind, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(x.Id, y.Id, StringComparison.OrdinalIgnoreCase);

    public int GetHashCode((string Kind, string Id) obj) =>
        HashCode.Combine(obj.Kind.ToLowerInvariant(), obj.Id.ToLowerInvariant());
}

/// <summary>
/// The three resolved maps a descriptor's <c>Rewrite</c> uses to translate a just-deserialized pack entity onto
/// this install: pack ids → the ids they were actually imported under, pack media file names → the freshly
/// saved local names, and source light keys → the target bulb chosen at import (or skip). All id/name lookups
/// are case-insensitive (ids and stored names are lowercase/NOCASE everywhere).
/// </summary>
public sealed class ShareRewriteContext
{
    /// <summary>(kind, packId) → the id that entity was imported under (may equal packId when it was free).</summary>
    public required IReadOnlyDictionary<(string Kind, string Id), string> IdMap { get; init; }

    /// <summary>Pack media file name → the freshly generated local file name it was saved as.</summary>
    public required IReadOnlyDictionary<string, string> MediaMap { get; init; }

    /// <summary>Source light key → target registered light key, or null to skip (drop) that binding.</summary>
    public required IReadOnlyDictionary<string, string?> LightKeyMap { get; init; }

    /// <summary>New id for a bundled dependency, or null when the ref was not part of the pack (dangling — the
    /// caller drops or nulls it).</summary>
    public string? MapDep(string kind, string? oldId)
    {
        if (string.IsNullOrEmpty(oldId)) return null;
        return IdMap.TryGetValue((kind, oldId), out var newId) ? newId : null;
    }

    /// <summary>New local file name for a bundled media reference, or null when it was not in the pack (the
    /// caller clears the ref).</summary>
    public string? MapMedia(string? oldName)
    {
        if (string.IsNullOrEmpty(oldName)) return null;
        return MediaMap.TryGetValue(oldName, out var newName) ? newName : null;
    }

    /// <summary>Target bulb key for a source light key, or null to skip. Any key absent from the map defaults to
    /// skip, so an unmapped binding is dropped rather than pointed at a random local bulb.</summary>
    public string? MapLightKey(string sourceKey) =>
        LightKeyMap.TryGetValue(sourceKey, out var target) ? target : null;
}
