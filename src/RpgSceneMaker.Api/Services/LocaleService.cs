using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using RpgSceneMaker.Api.Contracts;

namespace RpgSceneMaker.Api.Services;

/// <summary>
/// Serves the panel's UI translations. Each language is a plain JSON file on disk — one per BCP-47 code
/// (<c>en.json</c>, <c>pl.json</c>, …) in the locales directory — so the community or an AI agent can add
/// or edit a translation by dropping/editing a file, no rebuild. Files are read on demand (they are small
/// and this is a LAN app), so an edit shows up on the next language switch or reload.
///
/// English is also shipped <b>embedded in this assembly</b> as the canonical key set and the ultimate
/// fallback: a missing, deleted or broken on-disk file can never blank the UI. On startup <see cref="Seed"/>
/// copies the shipped files into the on-disk directory, but only the ones that are missing — it never
/// overwrites a community edit.
/// </summary>
public partial class LocaleService(string directory, ILogger<LocaleService> logger)
{
    public const string DefaultCode = "en";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // A BCP-47-ish code: a bare file name, so Path.Combine can never be talked into traversing directories.
    [GeneratedRegex(@"^[a-zA-Z]{2,3}(-[a-zA-Z0-9]{2,8})*$")]
    private static partial Regex CodePattern();

    // The on-disk file layout, minus the code (which comes from the file name).
    private record LocaleFile(string? Name, string? EnglishName, Dictionary<string, string>? Strings);

    /// <summary>Copy every shipped translation that is not already on disk into the locales directory.</summary>
    public void Seed()
    {
        try
        {
            Directory.CreateDirectory(directory);
            foreach (var (code, resource) in EmbeddedResources())
            {
                var dest = Path.Combine(directory, code + ".json");
                if (File.Exists(dest)) continue; // never clobber a community edit
                using var stream = typeof(LocaleService).Assembly.GetManifestResourceStream(resource)!;
                using var file = File.Create(dest);
                stream.CopyTo(file);
                logger.LogInformation("Seeded locale file {Code} into {Dir}", code, directory);
            }
        }
        catch (Exception ex)
        {
            // Seeding is best-effort; a read-only dir just means the embedded copies remain the fallback.
            logger.LogWarning(ex, "Could not seed locale files into {Dir}", directory);
        }
    }

    /// <summary>Languages the panel can switch to: on-disk files, plus any shipped code missing from disk.</summary>
    public IReadOnlyList<LocaleInfo> List()
    {
        var infos = new Dictionary<string, LocaleInfo>(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(directory))
        {
            foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
            {
                var code = Path.GetFileNameWithoutExtension(path);
                if (!CodePattern().IsMatch(code)) continue;
                if (ReadFile(path) is { } file)
                    infos[code] = ToInfo(code, file);
            }
        }

        // Guarantee the shipped languages appear even if a user deleted their on-disk file.
        foreach (var (code, resource) in EmbeddedResources())
            if (!infos.ContainsKey(code) && ReadEmbedded(resource) is { } file)
                infos[code] = ToInfo(code, file);

        // English first, then the rest alphabetically by English name.
        return [.. infos.Values
            .OrderBy(i => i.Code.Equals(DefaultCode, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(i => i.EnglishName, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>One language's document, or null if the code is unknown. On-disk wins; shipped is the fallback.</summary>
    public LocaleDocument? Get(string code)
    {
        if (!CodePattern().IsMatch(code)) return null;

        var path = Path.Combine(directory, code + ".json");
        var file = File.Exists(path) ? ReadFile(path) : null;

        // Fall back to the embedded shipped copy (this is what makes a deleted/broken en.json harmless).
        file ??= EmbeddedResources().FirstOrDefault(r =>
            r.Code.Equals(code, StringComparison.OrdinalIgnoreCase)) is { Resource: { } res }
            ? ReadEmbedded(res)
            : null;

        return file is null ? null : ToDocument(code, file);
    }

    private LocaleFile? ReadFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<LocaleFile>(stream, Json);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ignoring malformed locale file {Path}", path);
            return null;
        }
    }

    private LocaleFile? ReadEmbedded(string resource)
    {
        using var stream = typeof(LocaleService).Assembly.GetManifestResourceStream(resource);
        return stream is null ? null : JsonSerializer.Deserialize<LocaleFile>(stream, Json);
    }

    // The shipped translations embedded in this assembly, as (code, manifest-resource-name) pairs.
    private static IEnumerable<(string Code, string Resource)> EmbeddedResources()
    {
        const string marker = ".Locales.";
        foreach (var name in typeof(LocaleService).Assembly.GetManifestResourceNames())
        {
            var at = name.IndexOf(marker, StringComparison.Ordinal);
            if (at < 0 || !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            var code = name[(at + marker.Length)..^".json".Length];
            yield return (code, name);
        }
    }

    private static LocaleInfo ToInfo(string code, LocaleFile file) =>
        new(code, string.IsNullOrWhiteSpace(file.Name) ? code : file.Name,
            string.IsNullOrWhiteSpace(file.EnglishName) ? code : file.EnglishName);

    private static LocaleDocument ToDocument(string code, LocaleFile file) =>
        new(code, string.IsNullOrWhiteSpace(file.Name) ? code : file.Name,
            string.IsNullOrWhiteSpace(file.EnglishName) ? code : file.EnglishName,
            file.Strings ?? []);
}
