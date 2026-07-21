using AmbientDirector.Api.Models;
using AmbientDirector.Api.Validation;

namespace AmbientDirector.Api.Services.Sharing;

/// <summary>A party member (kind "player", matching the <c>/party/players</c> route vocabulary): carries its
/// portrait. Counters are portable; no dependencies.</summary>
public sealed class PartyMemberShareDescriptor(PartyStore store) : ShareDescriptor<PartyMember>
{
    public override string Kind => "player";
    protected override Task<PartyMember?> GetAsync(string id) => store.GetMemberAsync(id);
    protected override Task UpsertAsync(PartyMember member) => store.UpsertMemberAsync(member);
    protected override void Validate(PartyMember member) => PartyValidation.Validate(member);
    protected override string GetId(PartyMember member) => member.Id;
    protected override void SetId(PartyMember member, string id) => member.Id = id;
    protected override string GetName(PartyMember member) => member.Name;

    protected override IEnumerable<MediaRef> Media(PartyMember member)
    {
        if (!string.IsNullOrEmpty(member.Portrait)) yield return new(MediaKind.Image, member.Portrait!);
    }

    protected override void Rewrite(PartyMember member, ShareRewriteContext ctx) =>
        member.Portrait = ctx.MapMedia(member.Portrait);
}
