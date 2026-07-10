using RpgSceneMaker.Api.Models;
using RpgSceneMaker.Api.Services;
using RpgSceneMaker.Api.Validation;

namespace RpgSceneMaker.Api.Endpoints;

public static class ScreenEndpoints
{
    public static void MapScreenEndpoints(this WebApplication app)
    {
        // A screen is an organizational board of shortcuts, not an action, so there is no /trigger here —
        // its tiles just call the existing /scenes, /events, /sounds, /music and /lights endpoints.
        var screens = app.MapGroup("/screens");

        // Literal segment, so it wins over any "/{id}" route.
        screens.MapGet("/list", (ScreenStore store) => store.GetAllAsync());

        screens.MapPut("/{id}", async (string id, Screen screen, ScreenStore store) =>
        {
            screen.Id = id;
            ScreenValidation.Validate(screen);
            await store.UpsertAsync(screen);
            return Results.Ok(screen);
        });

        screens.MapDelete("/{id}", async (string id, ScreenStore store) =>
            await store.DeleteAsync(id) ? Results.NoContent() : Results.NotFound());

        // Deliberately NO "GET /screens/{id}" (and nothing at the bare "/screens"): the Blazor panel's
        // Screens list lives at /screens and each board at /screens/{id}, so a full-page load of either
        // must fall through to index.html. The panel reads the whole list from /screens/list and picks the
        // board by id client-side. (MapFallbackToFile only serves GET, so the PUT/DELETE above are safe.)
    }
}
