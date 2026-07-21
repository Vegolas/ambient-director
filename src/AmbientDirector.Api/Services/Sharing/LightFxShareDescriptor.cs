using AmbientDirector.Api.Models;
using AmbientDirector.Api.Validation;

namespace AmbientDirector.Api.Services.Sharing;

/// <summary>The simplest shareable kind: a pure keyframe sequence — no media, no dependencies, no bulb
/// bindings — so it imports verbatim (only its id may change to avoid a collision).</summary>
public sealed class LightFxShareDescriptor(LightFxStore store) : ShareDescriptor<LightFx>
{
    public override string Kind => "lightfx";
    protected override Task<LightFx?> GetAsync(string id) => store.GetAsync(id);
    protected override Task UpsertAsync(LightFx fx) => store.UpsertAsync(fx);
    protected override void Validate(LightFx fx) => LightFxValidation.Validate(fx);
    protected override string GetId(LightFx fx) => fx.Id;
    protected override void SetId(LightFx fx, string id) => fx.Id = id;
    protected override string GetName(LightFx fx) => fx.Name;
}
