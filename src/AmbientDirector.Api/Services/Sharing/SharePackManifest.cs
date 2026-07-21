using System.Text.Json;
using System.Text.Json.Serialization;

namespace AmbientDirector.Api.Services.Sharing;

/// <summary>Format constants for a share pack.</summary>
public static class SharePack
{
    /// <summary>The <c>manifest.json</c> <c>format</c> discriminator — import rejects anything else.</summary>
    public const string FormatId = "ambient-director/share-pack";

    /// <summary>The manifest version this build writes and the newest it can read. Import rejects a higher
    /// version (a pack from a future build), but reads older ones.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Zip entry name for the manifest.</summary>
    public const string ManifestEntry = "manifest.json";

    /// <summary>Zip sub-folders media lives under (also the traversal allowlist on import).</summary>
    public const string ImagesDir = "media/images/";
    public const string AudioDir = "media/audio/";
}

/// <summary>
/// The <c>manifest.json</c> at the root of a share-pack zip: what the pack contains, the light bindings that
/// need remapping, and the media files that travel alongside. Entities are stored as their full wire JSON (the
/// exact shape the HTTP endpoints emit, via <c>AiJson.Options</c>) so import deserializes straight into the
/// models and reuses the existing validators — and <see cref="Models.Sound"/> metadata (duration/waveform/
/// attribution) survives for free.
/// </summary>
public sealed record SharePackManifest
{
    public string Format { get; init; } = SharePack.FormatId;

    public int FormatVersion { get; init; } = SharePack.CurrentVersion;

    /// <summary>Informational only (which app/version wrote the pack); import never branches on it.</summary>
    public SharePackApp? App { get; init; }

    /// <summary>The root entity the user chose to share — for the import UI headline.</summary>
    public SharePackRef Primary { get; init; }

    /// <summary>Bundled entities keyed by kind; each value is the list of full entity JSON documents.
    /// Settable (not init) so import can normalize a <c>"entities": null</c> back to an empty map.</summary>
    public Dictionary<string, List<JsonElement>> Entities { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Distinct light keys referenced anywhere in the pack, each with the owner labels that used it.</summary>
    public List<SharePackLightKey> LightKeys { get; init; } = [];

    /// <summary>The media files bundled under <c>media/</c>, by stored name.</summary>
    public List<SharePackMedia> Media { get; init; } = [];
}

public sealed record SharePackApp(string Name, string? Version);

public readonly record struct SharePackRef(string Kind, string Id);

public sealed record SharePackLightKey(string Key, List<string> Sources);

/// <summary>One bundled media file: its stored name, whether it's an image or audio, and a free-text role hint
/// (e.g. <c>"scene:tavern:image"</c>) for diagnostics.</summary>
public sealed record SharePackMedia(string Name, [property: JsonConverter(typeof(JsonStringEnumConverter))] MediaKind Kind, string? Role);
