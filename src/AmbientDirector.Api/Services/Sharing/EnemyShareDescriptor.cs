using AmbientDirector.Api.Models;
using AmbientDirector.Api.Validation;

namespace AmbientDirector.Api.Services.Sharing;

/// <summary>A bestiary statblock (kind "enemy"): carries its portrait. Base counters are portable; no
/// dependencies. Shares <see cref="PartyStore"/> with the party members.</summary>
public sealed class EnemyShareDescriptor(PartyStore store) : ShareDescriptor<Enemy>
{
    public override string Kind => "enemy";
    protected override Task<Enemy?> GetAsync(string id) => store.GetEnemyAsync(id);
    protected override Task UpsertAsync(Enemy enemy) => store.UpsertEnemyAsync(enemy);
    protected override void Validate(Enemy enemy) => PartyValidation.Validate(enemy);
    protected override string GetId(Enemy enemy) => enemy.Id;
    protected override void SetId(Enemy enemy, string id) => enemy.Id = id;
    protected override string GetName(Enemy enemy) => enemy.Name;

    protected override IEnumerable<MediaRef> Media(Enemy enemy)
    {
        if (!string.IsNullOrEmpty(enemy.Portrait)) yield return new(MediaKind.Image, enemy.Portrait!);
    }

    protected override void Rewrite(Enemy enemy, ShareRewriteContext ctx) =>
        enemy.Portrait = ctx.MapMedia(enemy.Portrait);
}
