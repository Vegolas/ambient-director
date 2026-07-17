namespace RpgSceneMaker.Api.Services;

/// <summary>
/// Scrubs cross-entity references when a scene/event/sound is deleted, so nothing is left dangling. Screens
/// hold shortcut tiles that point at those entities by id, and an event's <c>After</c> can activate a scene by
/// id; deleting the target used to leave a permanent "missing" tile / a dangling After. Shared by the delete
/// endpoints and the AI tool façade so both delete the same way. Mirrors how deleting a sound scrubs its id
/// from scenes/events (<see cref="Endpoints.SoundEndpoints"/>) and how <see cref="LightFxDetacher"/> detaches
/// FX references.
/// </summary>
public static class ReferenceScrubber
{
    /// <summary>Remove every screen tile that points at the deleted entity, matched by kind + id
    /// (case-insensitively, like the stores' NOCASE ids). Only the screens actually touched are saved.
    /// <paramref name="kind"/> is the <see cref="Models.ScreenTile.Kind"/> — <c>scene</c>/<c>event</c>/<c>sound</c>;
    /// music tiles carry a Spotify URI, not an entity id, so they are never matched.</summary>
    public static async Task ScrubScreenTilesAsync(ScreenStore screens, string kind, string id)
    {
        foreach (var screen in await screens.GetAllAsync())
        {
            var kept = screen.Tiles
                .Where(t => !(string.Equals(t.Kind, kind, StringComparison.OrdinalIgnoreCase)
                              && string.Equals(t.Ref, id, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (kept.Count != screen.Tiles.Count)
            {
                screen.Tiles = kept;
                await screens.UpsertAsync(screen);
            }
        }
    }

    /// <summary>Reset any event whose <c>After</c> activates the now-deleted scene back to the historical
    /// default (restore prior lighting) by clearing <see cref="Models.GameEvent.After"/> — null and Mode
    /// "previous" are equivalent. Only the events actually touched are saved.</summary>
    public static async Task ScrubEventAfterSceneAsync(EventStore events, string sceneId)
    {
        foreach (var evt in await events.GetAllAsync())
        {
            if (evt.After is { Mode: "scene" } after
                && string.Equals(after.SceneId, sceneId, StringComparison.OrdinalIgnoreCase))
            {
                evt.After = null;
                await events.UpsertAsync(evt);
            }
        }
    }

    /// <summary>Drop every scene per-light entry (<see cref="Models.SceneLight"/>) and event-timeline light
    /// clip (<see cref="Models.TimelineLightClip"/>) that targets a registered light key that was just removed
    /// from the config, so activations no longer log "unknown light, skipped" for a key that no longer exists.
    /// Entries/clips with no key ("all lights" via the provider group) are never touched. An event timeline
    /// left with no clips at all is dropped back to a legacy (null-timeline) event, matching the sound scrub.
    /// Only the scenes/events actually changed are saved. <paramref name="removedKeys"/> must compare
    /// case-insensitively (light keys use NOCASE collation).</summary>
    public static async Task ScrubRemovedLightKeysAsync(SceneStore scenes, EventStore events, IReadOnlySet<string> removedKeys)
    {
        if (removedKeys.Count == 0) return;

        foreach (var scene in await scenes.GetAllAsync())
        {
            var kept = scene.Lights.Where(l => !removedKeys.Contains(l.LightKey)).ToList();
            if (kept.Count != scene.Lights.Count)
            {
                scene.Lights = kept;
                await scenes.UpsertAsync(scene);
            }
        }

        foreach (var evt in await events.GetAllAsync())
        {
            if (evt.Timeline is not { } timeline) continue;
            var keptClips = timeline.Lights
                .Where(c => string.IsNullOrEmpty(c.LightKey) || !removedKeys.Contains(c.LightKey))
                .ToList();
            if (keptClips.Count == timeline.Lights.Count) continue;
            timeline.Lights = keptClips;
            // A timeline stripped of all clips triggers as a silent no-op — drop it back to a legacy event.
            if (timeline.Sounds.Count == 0 && timeline.Lights.Count == 0)
                evt.Timeline = null;
            await events.UpsertAsync(evt);
        }
    }
}
