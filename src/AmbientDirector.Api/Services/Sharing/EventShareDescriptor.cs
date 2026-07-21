using AmbientDirector.Api.Models;
using AmbientDirector.Api.Validation;

namespace AmbientDirector.Api.Services.Sharing;

/// <summary>A one-shot event: bundles its sounds, timeline Light FX and (for a "scene" ending) the target
/// scene; carries its tile art; exposes its timeline light-clip bulb bindings for the remap step.</summary>
public sealed class EventShareDescriptor(EventStore store) : ShareDescriptor<GameEvent>
{
    public override string Kind => "event";
    protected override Task<GameEvent?> GetAsync(string id) => store.GetAsync(id);
    protected override Task UpsertAsync(GameEvent evt) => store.UpsertAsync(evt);
    protected override void Validate(GameEvent evt) => EventValidation.Validate(evt);
    protected override string GetId(GameEvent evt) => evt.Id;
    protected override void SetId(GameEvent evt, string id) => evt.Id = id;
    protected override string GetName(GameEvent evt) => evt.Name;

    protected override IEnumerable<MediaRef> Media(GameEvent evt)
    {
        if (!string.IsNullOrEmpty(evt.Image)) yield return new(MediaKind.Image, evt.Image!);
    }

    protected override IEnumerable<DepRef> Dependencies(GameEvent evt)
    {
        foreach (var soundId in evt.SoundEffects ?? [])
            if (!string.IsNullOrEmpty(soundId)) yield return new("sound", soundId);
        if (evt.Timeline is { } timeline)
        {
            foreach (var clip in timeline.Sounds ?? [])
                if (!string.IsNullOrEmpty(clip.SoundId)) yield return new("sound", clip.SoundId);
            foreach (var clip in timeline.Lights ?? [])
                if (clip.Effect is { Type: "fx", FxId: { } fxId } && !string.IsNullOrEmpty(fxId))
                    yield return new("lightfx", fxId);
        }
        if (evt.After is { Mode: "scene", SceneId: { } sceneId } && !string.IsNullOrEmpty(sceneId))
            yield return new("scene", sceneId);
    }

    protected override IEnumerable<LightKeySite> LightKeys(GameEvent evt)
    {
        foreach (var clip in evt.Timeline?.Lights ?? [])
            if (!string.IsNullOrEmpty(clip.LightKey))   // null/empty = "all lights", not a per-bulb binding
                yield return new(clip.LightKey!, $"Event '{evt.Name}'");
    }

    protected override void Rewrite(GameEvent evt, ShareRewriteContext ctx)
    {
        evt.Image = ctx.MapMedia(evt.Image);

        evt.SoundEffects = (evt.SoundEffects ?? [])
            .Select(id => ctx.MapDep("sound", id)).OfType<string>().ToList();

        if (evt.After is { Mode: "scene", SceneId: { } sceneId })
        {
            var newScene = ctx.MapDep("scene", sceneId);
            if (newScene is null) evt.After = null;   // dangling target → drop the ending (else afterSceneId 400)
            else evt.After.SceneId = newScene;
        }

        if (evt.Timeline is { } timeline)
        {
            var keptSounds = new List<TimelineSoundClip>();
            foreach (var clip in timeline.Sounds ?? [])
            {
                var newSound = ctx.MapDep("sound", clip.SoundId);
                if (newSound is null) continue;        // dangling sound → drop the clip
                clip.SoundId = newSound;
                keptSounds.Add(clip);
            }
            timeline.Sounds = keptSounds;

            var keptLights = new List<TimelineLightClip>();
            foreach (var clip in timeline.Lights ?? [])
            {
                if (string.IsNullOrEmpty(clip.LightKey))
                {
                    RemapClipFx(clip, ctx);            // all-lights clip: no per-bulb binding
                    keptLights.Add(clip);
                    continue;
                }
                var target = ctx.MapLightKey(clip.LightKey);
                if (target is null) continue;          // skipped / unmapped → drop the clip
                clip.LightKey = target;
                RemapClipFx(clip, ctx);
                keptLights.Add(clip);
            }
            timeline.Lights = keptLights;

            // A timeline emptied by the remaps would fail validation (timelineEmpty) and do nothing — drop it,
            // restoring the legacy no-timeline shape.
            if (timeline.Sounds.Count == 0 && timeline.Lights.Count == 0) evt.Timeline = null;
        }
    }

    private static void RemapClipFx(TimelineLightClip clip, ShareRewriteContext ctx)
    {
        if (clip.Effect is { Type: "fx", FxId: { } fxId })
        {
            var newFx = ctx.MapDep("lightfx", fxId);
            if (newFx is null) clip.Effect = null;
            else clip.Effect.FxId = newFx;
        }
    }
}
