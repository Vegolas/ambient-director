using AmbientDirector.Api.Models;
using AmbientDirector.Api.Validation;

namespace AmbientDirector.Api.Services.Sharing;

/// <summary>A player-facing TV board: carries its background + image-element art. Has no entity dependencies
/// (its party/enemies elements are live placeholders rendered from the local roster at TV time).</summary>
public sealed class BoardShareDescriptor(BoardStore store) : ShareDescriptor<Board>
{
    public override string Kind => "board";
    protected override Task<Board?> GetAsync(string id) => store.GetAsync(id);
    protected override Task UpsertAsync(Board board) => store.UpsertAsync(board);
    protected override void Validate(Board board) => BoardValidation.Validate(board);
    protected override string GetId(Board board) => board.Id;
    protected override void SetId(Board board, string id) => board.Id = id;
    protected override string GetName(Board board) => board.Name;

    protected override IEnumerable<MediaRef> Media(Board board) =>
        board.ReferencedFiles().Select(name => new MediaRef(MediaKind.Image, name));

    protected override void Rewrite(Board board, ShareRewriteContext ctx)
    {
        board.BackgroundImage = ctx.MapMedia(board.BackgroundImage); // missing → null (falls back to colour/black)

        var kept = new List<BoardElement>();
        foreach (var element in board.Elements ?? [])
        {
            if (element.Kind == "image")
            {
                var newImage = ctx.MapMedia(element.Image);
                if (newImage is null) continue;   // bundled image missing → drop the element (else validation fails)
                element.Image = newImage;
            }
            kept.Add(element);
        }
        board.Elements = kept;
    }
}
