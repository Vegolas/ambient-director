using AmbientDirector.Api.Models;
using AmbientDirector.Api.Services;
using AmbientDirector.Api.Validation;

namespace AmbientDirector.Api.Endpoints;

public static class BoardEndpoints
{
    public static void MapBoardEndpoints(this WebApplication app)
    {
        // A board is composable player-facing TV content (a 16:9 layout the GM pushes to /tv), not an action,
        // so there is no /trigger here — the panel puts a board on the display via /tv/show?board={id}.
        var boards = app.MapGroup("/boards");

        // Literal segment, so it wins over any "/{id}" route.
        boards.MapGet("/list", (BoardStore store) => store.GetAllAsync());

        boards.MapPut("/{id}", async (string id, Board board, BoardStore store, ImageFileStorage images, TvState tvState) =>
        {
            board.Id = id;
            BoardValidation.Validate(board);
            // Own the board's uploads and clean up on replace: capture the files this board USED to reference,
            // and after the save drop any that it no longer references (the Scene/Event/Screen ownership +
            // cleanup pattern, generalized from a single Image to a whole file set).
            var oldFiles = (await store.GetAsync(id))?.ReferencedFiles()
                .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
            await store.UpsertAsync(board);
            var newFiles = board.ReferencedFiles().ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var stale in oldFiles.Where(f => !newFiles.Contains(f)))
                images.Delete(stale);
            // Live edit: if this board is the one currently on the TV, bump the rev so an open display
            // re-renders within one 2 s poll (the codebase's real-time idiom — no SSE).
            tvState.TouchBoard(id);
            return Results.Ok(board);
        });

        boards.MapDelete("/{id}", async (string id, BoardStore store, ImageFileStorage images, TvState tvState) =>
        {
            // Capture the referenced files before deleting so we can release the board's uploads afterwards.
            var files = (await store.GetAsync(id))?.ReferencedFiles().ToList();
            if (!await store.DeleteAsync(id)) return Results.NotFound();
            foreach (var file in files ?? [])
                images.Delete(file);
            // Nothing dangles after a delete: clear the display if this board was showing, and scrub it from
            // Recent (mirrors the sound-delete scrub).
            tvState.ForgetBoard(id);
            return Results.NoContent();
        });

        // Deliberately NO "GET /boards/{id}" (and nothing at the bare "/boards"): the Blazor panel's Boards
        // list lives at /boards and each board editor at /boards/{id}, so a full-page load of either must fall
        // through to index.html. The panel reads the whole list from /boards/list and picks the board by id
        // client-side. (MapFallbackToFile only serves GET, so the PUT/DELETE above are safe.)
    }
}
