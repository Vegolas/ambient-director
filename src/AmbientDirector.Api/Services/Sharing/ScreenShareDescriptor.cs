using AmbientDirector.Api.Models;
using AmbientDirector.Api.Validation;

namespace AmbientDirector.Api.Services.Sharing;

/// <summary>A shortcut board: bundles the scenes/events/sounds its tiles point at and remaps their refs on
/// import; music tiles (portable Spotify URIs) and command/break tiles pass through untouched.</summary>
public sealed class ScreenShareDescriptor(ScreenStore store) : ShareDescriptor<Screen>
{
    public override string Kind => "screen";
    protected override Task<Screen?> GetAsync(string id) => store.GetAsync(id);
    protected override Task UpsertAsync(Screen screen) => store.UpsertAsync(screen);
    protected override void Validate(Screen screen) => ScreenValidation.Validate(screen);
    protected override string GetId(Screen screen) => screen.Id;
    protected override void SetId(Screen screen, string id) => screen.Id = id;
    protected override string GetName(Screen screen) => screen.Name;

    protected override IEnumerable<MediaRef> Media(Screen screen)
    {
        if (!string.IsNullOrEmpty(screen.Image)) yield return new(MediaKind.Image, screen.Image!);
    }

    protected override IEnumerable<DepRef> Dependencies(Screen screen)
    {
        foreach (var tile in screen.Tiles ?? [])
            if (TileDepKind(tile.Kind) is { } depKind && !string.IsNullOrEmpty(tile.Ref))
                yield return new(depKind, tile.Ref);
    }

    protected override void Rewrite(Screen screen, ShareRewriteContext ctx)
    {
        screen.Image = ctx.MapMedia(screen.Image);
        var kept = new List<ScreenTile>();
        foreach (var tile in screen.Tiles ?? [])
        {
            var depKind = TileDepKind(tile.Kind);
            if (depKind is null) { kept.Add(tile); continue; } // music / light-reset / break: portable
            var newRef = ctx.MapDep(depKind, tile.Ref);
            if (newRef is null) continue;                      // dangling target → drop the tile
            tile.Ref = newRef;
            kept.Add(tile);
        }
        screen.Tiles = kept;
    }

    // The tile kinds whose Ref is a bundled entity id; null for portable tiles (music URI, commands, break).
    private static string? TileDepKind(string kind) => kind switch
    {
        "scene" => "scene",
        "event" => "event",
        "sound" => "sound",
        _ => null,
    };
}
