using Microsoft.EntityFrameworkCore;
using RpgSceneMaker.Api.Data;

namespace RpgSceneMaker.Api.Services;

/// <summary>
/// Anthropic (BYOK) assistant settings persisted in SQLite. Mirrors <see cref="SpotifyStore"/>: the current
/// value is cached in memory and swapped atomically on save, so changes apply immediately and reads never
/// race a database reload. The API key is write-only from the panel's point of view — it is stored here and
/// used to talk to the Anthropic API, but never returned by any endpoint.
/// </summary>
public class AnthropicStore(IDbContextFactory<AppDbContext> dbFactory)
{
    private readonly Lock _lock = new();
    private AnthropicConfig? _current;

    public AnthropicConfig Current
    {
        get
        {
            lock (_lock) { return _current ??= Load(); }
        }
    }

    /// <summary>Save the key and/or model. A null/empty key keeps the stored one (so the model can change
    /// without re-pasting the key); a non-empty key or model replaces the stored value after trimming.</summary>
    public void Save(string? apiKey, string? model) =>
        Update(entity =>
        {
            if (!string.IsNullOrWhiteSpace(apiKey))
                entity.ApiKey = apiKey.Trim();
            if (!string.IsNullOrWhiteSpace(model))
                entity.Model = model.Trim();
        });

    /// <summary>Forget the API key (the assistant goes back to unconfigured). The model is kept.</summary>
    public void Clear() =>
        Update(entity => entity.ApiKey = "");

    private void Update(Action<AnthropicConfig> mutate)
    {
        lock (_lock)
        {
            using var db = dbFactory.CreateDbContext();
            var entity = db.AnthropicConfigs.SingleOrDefault(c => c.Id == AnthropicConfig.SingletonId);
            if (entity is null)
            {
                entity = new AnthropicConfig();
                db.AnthropicConfigs.Add(entity);
            }

            mutate(entity);
            db.SaveChanges();
            _current = entity;
        }
    }

    private AnthropicConfig Load()
    {
        using var db = dbFactory.CreateDbContext();
        return db.AnthropicConfigs.AsNoTracking().SingleOrDefault(c => c.Id == AnthropicConfig.SingletonId)
               ?? new AnthropicConfig();
    }
}
