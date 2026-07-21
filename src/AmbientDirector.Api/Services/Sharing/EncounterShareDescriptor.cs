using AmbientDirector.Api.Models;
using AmbientDirector.Api.Validation;

namespace AmbientDirector.Api.Services.Sharing;

/// <summary>A prepped fight: bundles its heroes (party members), enemy statblocks, and the scene/event it
/// activates on run; carries its background + enemy-instance portrait snapshots.</summary>
public sealed class EncounterShareDescriptor(EncounterStore store) : ShareDescriptor<Encounter>
{
    public override string Kind => "encounter";
    protected override Task<Encounter?> GetAsync(string id) => store.GetAsync(id);
    protected override Task UpsertAsync(Encounter encounter) => store.UpsertAsync(encounter);
    protected override void Validate(Encounter encounter) => EncounterValidation.Validate(encounter);
    protected override string GetId(Encounter encounter) => encounter.Id;
    protected override void SetId(Encounter encounter, string id) => encounter.Id = id;
    protected override string GetName(Encounter encounter) => encounter.Name;

    protected override IEnumerable<MediaRef> Media(Encounter encounter)
    {
        if (!string.IsNullOrEmpty(encounter.BackgroundImage))
            yield return new(MediaKind.Image, encounter.BackgroundImage!);
        foreach (var instance in encounter.Enemies ?? [])
            if (!string.IsNullOrEmpty(instance.Portrait))
                yield return new(MediaKind.Image, instance.Portrait!);
        // Hero portraits live on the bundled party members, owned there — not enumerated here.
    }

    protected override IEnumerable<DepRef> Dependencies(Encounter encounter)
    {
        if (!string.IsNullOrEmpty(encounter.ActivateSceneId)) yield return new("scene", encounter.ActivateSceneId!);
        if (!string.IsNullOrEmpty(encounter.ActivateEventId)) yield return new("event", encounter.ActivateEventId!);
        foreach (var heroId in encounter.HeroIds ?? [])
            if (!string.IsNullOrEmpty(heroId)) yield return new("player", heroId);
        foreach (var instance in encounter.Enemies ?? [])
            if (!string.IsNullOrEmpty(instance.EnemyId)) yield return new("enemy", instance.EnemyId);
    }

    protected override void Rewrite(Encounter encounter, ShareRewriteContext ctx)
    {
        encounter.BackgroundImage = ctx.MapMedia(encounter.BackgroundImage);
        encounter.ActivateSceneId = ctx.MapDep("scene", encounter.ActivateSceneId);
        encounter.ActivateEventId = ctx.MapDep("event", encounter.ActivateEventId);
        encounter.HeroIds = (encounter.HeroIds ?? [])
            .Select(h => ctx.MapDep("player", h)).OfType<string>().ToList();

        foreach (var instance in encounter.Enemies ?? [])
        {
            instance.Portrait = ctx.MapMedia(instance.Portrait);
            // EnemyId can't be blank (EncounterValidation) → remap if bundled, else keep the old id
            // (an instance whose template was deleted at the source is a tolerated stand-alone state).
            instance.EnemyId = ctx.MapDep("enemy", instance.EnemyId) ?? instance.EnemyId;
        }
    }
}
