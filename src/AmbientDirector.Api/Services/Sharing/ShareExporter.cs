using System.IO.Compression;
using System.Text;
using System.Text.Json;
using AmbientDirector.Api.Errors;
using AmbientDirector.Api.Services.Ai;

namespace AmbientDirector.Api.Services.Sharing;

/// <summary>The temp zip an export produced: the download file name and the on-disk path the endpoint streams
/// (then deletes on close).</summary>
public readonly record struct ShareExportResult(string FileName, string TempPath);

/// <summary>
/// Builds a shareable <c>.zip</c> pack for a root entity: walks its transitive dependency closure (cycle-safe),
/// collects the union of referenced media files, and writes a manifest + the media into a temp zip. Reuses the
/// per-kind <see cref="IShareDescriptor"/>s and the on-disk file stores. Singleton — every dependency
/// (registry, file stores) is a singleton.
/// </summary>
public sealed class ShareExporter(ShareRegistry registry, ImageFileStorage images, SoundFileStorage sounds)
{
    public async Task<ShareExportResult> ExportAsync(string kind, string id)
    {
        var rootDescriptor = registry.Get(kind);
        var root = await rootDescriptor.LoadAsync(id)
            ?? throw new NotFoundException("error.share.entityNotFound", kind, id);
        var rootId = rootDescriptor.IdOf(root); // canonical (NOCASE-normalized) id

        var collected = await CollectClosureAsync(kind, rootId, root);

        // Group the collected entities by kind as their full wire JSON.
        var entities = new Dictionary<string, List<JsonElement>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (curKind, entity) in collected)
        {
            if (!entities.TryGetValue(curKind, out var list))
                entities[curKind] = list = [];
            list.Add(registry.Get(curKind).ToJson(entity));
        }

        // Distinct light-key bindings across the pack, each with the owner labels that used it.
        var lightKeys = new Dictionary<string, SharePackLightKey>(StringComparer.OrdinalIgnoreCase);
        foreach (var (curKind, entity) in collected)
            foreach (var site in registry.Get(curKind).LightKeys(entity))
            {
                if (string.IsNullOrEmpty(site.Key)) continue;
                if (!lightKeys.TryGetValue(site.Key, out var existing))
                    lightKeys[site.Key] = existing = new SharePackLightKey(site.Key, []);
                if (!existing.Sources.Contains(site.OwnerLabel))
                    existing.Sources.Add(site.OwnerLabel);
            }

        // Union of referenced media files, deduped by stored name (an enemy-instance portrait and its template
        // share one file → one entry). Role is a diagnostic "owner kind:id" hint only.
        var media = new Dictionary<string, (MediaKind Kind, string Role)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (curKind, entity) in collected)
        {
            var ownerId = registry.Get(curKind).IdOf(entity);
            foreach (var m in registry.Get(curKind).Media(entity))
                if (!string.IsNullOrEmpty(m.StoredName))
                    media.TryAdd(m.StoredName, (m.Kind, $"{curKind}:{ownerId}"));
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "ambient-director-share");
        Directory.CreateDirectory(tempDir);
        var tempPath = Path.Combine(tempDir, Guid.NewGuid().ToString("N") + ".zip");

        var writtenMedia = new List<SharePackMedia>();
        await using (var zipStream = File.Create(tempPath))
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Media first, so the manifest lists only files that actually made it into the zip (a referenced
            // file missing on disk is simply omitted; import then clears that reference).
            foreach (var (name, m) in media)
            {
                var path = m.Kind == MediaKind.Audio ? sounds.FullPathForName(name) : images.FullPathForName(name);
                if (path is null || !File.Exists(path)) continue;
                var dir = m.Kind == MediaKind.Audio ? SharePack.AudioDir : SharePack.ImagesDir;
                var entry = archive.CreateEntry(dir + name, CompressionLevel.NoCompression); // already-compressed media
                await using (var entryStream = entry.Open())
                await using (var fileStream = File.OpenRead(path))
                    await fileStream.CopyToAsync(entryStream);
                writtenMedia.Add(new SharePackMedia(name, m.Kind, m.Role));
            }

            var manifest = new SharePackManifest
            {
                Primary = new SharePackRef(kind, rootId),
                App = new SharePackApp("Ambient Director", null),
                Entities = entities,
                LightKeys = lightKeys.Values.ToList(),
                Media = writtenMedia,
            };
            var manifestEntry = archive.CreateEntry(SharePack.ManifestEntry, CompressionLevel.Optimal);
            await using var manifestStream = manifestEntry.Open();
            await JsonSerializer.SerializeAsync(manifestStream, manifest, AiJson.Options);
        }

        return new ShareExportResult($"{Slugify(rootDescriptor.NameOf(root), kind)}-{kind}.zip", tempPath);
    }

    // BFS over the dependency edges. `visited` keys on (kind, canonical-id) case-insensitively so cycles
    // (event → after-scene → screen tile → event …) terminate and each entity is bundled once. A dependency
    // that no longer exists (a ref to a since-deleted entity) is simply skipped — import re-normalizes it.
    private async Task<List<(string Kind, object Entity)>> CollectClosureAsync(string kind, string rootId, object root)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { VisitKey(kind, rootId) };
        var collected = new List<(string Kind, object Entity)>();
        var queue = new Queue<(string Kind, object Entity)>();
        queue.Enqueue((kind, root));

        while (queue.Count > 0)
        {
            var (curKind, entity) = queue.Dequeue();
            collected.Add((curKind, entity));
            foreach (var dep in registry.Get(curKind).Dependencies(entity))
            {
                if (!registry.Has(dep.Kind)) continue;
                var depDescriptor = registry.Get(dep.Kind);
                var depEntity = await depDescriptor.LoadAsync(dep.Id);
                if (depEntity is null) continue; // dangling — skip
                if (!visited.Add(VisitKey(dep.Kind, depDescriptor.IdOf(depEntity)))) continue;
                queue.Enqueue((dep.Kind, depEntity));
            }
        }
        return collected;
    }

    private static string VisitKey(string kind, string id) => $"{kind} {id}".ToLowerInvariant();

    // Filesystem-safe download name; falls back to the kind when the entity name has no usable characters.
    private static string Slugify(string s, string fallback)
    {
        var sb = new StringBuilder();
        foreach (var ch in (s ?? "").Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch)) sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? fallback : slug;
    }
}
