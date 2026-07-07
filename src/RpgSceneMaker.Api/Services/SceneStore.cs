using System.Text.Json;
using Microsoft.Extensions.Options;
using RpgSceneMaker.Api.Models;

namespace RpgSceneMaker.Api.Services;

/// <summary>Scenes persisted in a human-editable JSON file. The file is re-read when it changes on disk.</summary>
public class SceneStore(IOptions<SceneOptions> options, IHostEnvironment env)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly Lock _lock = new();
    private List<Scene>? _cache;
    private DateTime _cacheTimestamp;

    private string FilePath => Path.GetFullPath(Path.Combine(env.ContentRootPath, options.Value.FilePath));

    public IReadOnlyList<Scene> GetAll()
    {
        lock (_lock) { return Load(); }
    }

    public Scene? Get(string id)
    {
        lock (_lock)
        {
            return Load().FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void Upsert(Scene scene)
    {
        lock (_lock)
        {
            var scenes = Load();
            scenes.RemoveAll(s => s.Id.Equals(scene.Id, StringComparison.OrdinalIgnoreCase));
            scenes.Add(scene);
            Save(scenes);
        }
    }

    public bool Delete(string id)
    {
        lock (_lock)
        {
            var scenes = Load();
            var removed = scenes.RemoveAll(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed) Save(scenes);
            return removed;
        }
    }

    private List<Scene> Load()
    {
        if (!File.Exists(FilePath))
        {
            _cache = [];
            return _cache;
        }

        var timestamp = File.GetLastWriteTimeUtc(FilePath);
        if (_cache is null || timestamp != _cacheTimestamp)
        {
            _cache = JsonSerializer.Deserialize<List<Scene>>(File.ReadAllText(FilePath), JsonOpts) ?? [];
            _cacheTimestamp = timestamp;
        }
        return _cache;
    }

    private void Save(List<Scene> scenes)
    {
        File.WriteAllText(FilePath, JsonSerializer.Serialize(scenes, JsonOpts));
        _cache = scenes;
        _cacheTimestamp = File.GetLastWriteTimeUtc(FilePath);
    }
}
