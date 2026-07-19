using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace AmbientDirector.Tests.Integration;

/// <summary>
/// Boards (Phase 2 of #88): persisted, composable player-facing TV content. Covers the CRUD round-trip,
/// validation codes, owned-image cleanup, the show → /tv/state render projection, live-edit + delete
/// propagation, and — most importantly — the key-free gate invariant: the open TV surface may serve ONLY the
/// images the currently-shown board references, never the general /images route.
/// </summary>
[Collection("integration")]
public class BoardTests
{
    private const string Key = "s3cret";

    // A 1x1 transparent PNG — the smallest valid upload for the /images → board flow.
    private const string TinyPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

    private static async Task<string> UploadPngAsync(HttpClient client, string? apiKey = null)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Convert.FromBase64String(TinyPngBase64));
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "art.png");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/images/upload") { Content = form };
        if (apiKey is not null) request.Headers.Add("X-Api-Key", apiKey);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("id").GetString()!;
    }

    private static async Task<HttpResponseMessage> PutBoardAsync(HttpClient client, string id, object body, string? apiKey = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/boards/{id}") { Content = JsonContent.Create(body) };
        if (apiKey is not null) request.Headers.Add("X-Api-Key", apiKey);
        return await client.SendAsync(request);
    }

    // ---- 1. CRUD round-trip ----

    [Fact]
    public async Task Put_then_list_round_trips_the_board_with_elements_and_delete_removes_it()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        var img = await UploadPngAsync(client);

        (await client.PutAsJsonAsync("/boards/tavern", new
        {
            name = "Tavern",
            backgroundColor = "#a1b2c3", // lower-case → normalized to upper
            elements = new object[]
            {
                new { kind = "image", x = 10.0, y = 10.0, w = 30.0, h = 30.0, image = img },
                new { kind = "text", x = 50.0, y = 50.0, w = 40.0, h = 20.0, text = "Welcome", color = "#ffffff", size = 5.0, align = "center" },
            },
        })).EnsureSuccessStatusCode();

        var list = await client.GetFromJsonAsync<JsonElement>("/boards/list");
        var board = list.EnumerateArray().Single(b => b.GetProperty("id").GetString() == "tavern");
        Assert.Equal("Tavern", board.GetProperty("name").GetString());
        Assert.Equal("#A1B2C3", board.GetProperty("backgroundColor").GetString());

        var elements = board.GetProperty("elements");
        Assert.Equal(2, elements.GetArrayLength());
        Assert.Equal("image", elements[0].GetProperty("kind").GetString());
        Assert.Equal(img, elements[0].GetProperty("image").GetString());
        Assert.Equal(10.0, elements[0].GetProperty("x").GetDouble());
        Assert.Equal(30.0, elements[0].GetProperty("w").GetDouble());
        Assert.Equal("text", elements[1].GetProperty("kind").GetString());
        Assert.Equal("Welcome", elements[1].GetProperty("text").GetString());
        Assert.Equal("#FFFFFF", elements[1].GetProperty("color").GetString());
        Assert.Equal(5.0, elements[1].GetProperty("size").GetDouble());
        Assert.Equal("center", elements[1].GetProperty("align").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/boards/tavern")).StatusCode);
        var after = await client.GetFromJsonAsync<JsonElement>("/boards/list");
        Assert.DoesNotContain(after.EnumerateArray(), b => b.GetProperty("id").GetString() == "tavern");

        // Deleting a missing board is a 404.
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync("/boards/tavern")).StatusCode);
    }

    [Fact]
    public async Task Get_one_board_is_not_an_api_route_so_full_page_loads_reach_the_spa()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        (await client.PutAsJsonAsync("/boards/tavern", new { name = "Tavern" })).EnsureSuccessStatusCode();

        var response = await client.GetAsync("/boards/tavern");

        // There is deliberately no GET /boards/{id} API route (the panel reads /boards/list and picks by id),
        // so a full-page GET must NOT return JSON — it falls through to the SPA host instead.
        Assert.NotEqual("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    // ---- 2. Validation → 400 with stable codes ----

    [Fact]
    public async Task Invalid_boards_are_rejected_with_stable_codes()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        async Task AssertCode(object body, string expectedCode)
        {
            var resp = await client.PutAsJsonAsync("/boards/bad", body);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var problem = await resp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(expectedCode, problem.GetProperty("code").GetString());
        }

        // Unknown element kind.
        await AssertCode(new { name = "B", elements = new object[] {
            new { kind = "widget", x = 1.0, y = 1.0, w = 1.0, h = 1.0 } } }, "error.board.unknownKind");

        // Out-of-range geometry (X > 100).
        await AssertCode(new { name = "B", elements = new object[] {
            new { kind = "text", x = 150.0, y = 1.0, w = 1.0, h = 1.0, text = "hi" } } }, "error.board.elementBounds");

        // Text element with no text.
        await AssertCode(new { name = "B", elements = new object[] {
            new { kind = "text", x = 1.0, y = 1.0, w = 1.0, h = 1.0, text = "   " } } }, "error.board.textRequired");

        // Image element with a traversal file name.
        await AssertCode(new { name = "B", elements = new object[] {
            new { kind = "image", x = 1.0, y = 1.0, w = 1.0, h = 1.0, image = "../secret.png" } } }, "error.board.elementImage");

        // More than 50 elements.
        var many = Enumerable.Range(0, 51)
            .Select(_ => (object)new { kind = "text", x = 1.0, y = 1.0, w = 1.0, h = 1.0, text = "x" }).ToArray();
        await AssertCode(new { name = "B", elements = many }, "error.board.tooManyElements");

        // Bad background hex.
        await AssertCode(new { name = "B", backgroundColor = "zzz" }, "error.common.hexColor");

        // Bad text alignment.
        await AssertCode(new { name = "B", elements = new object[] {
            new { kind = "text", x = 1.0, y = 1.0, w = 1.0, h = 1.0, text = "hi", align = "middle" } } }, "error.board.textAlign");

        // A non-finite coordinate (NaN) must be caught by the IsFinite guard, not slip through. The client
        // can't serialize double.NaN under web defaults, so send raw JSON — "NaN" reads back as NaN.
        const string rawNaN = "{\"name\":\"B\",\"elements\":[{\"kind\":\"text\",\"x\":\"NaN\",\"y\":1,\"w\":1,\"h\":1,\"text\":\"hi\"}]}";
        using var content = new StringContent(rawNaN, Encoding.UTF8, "application/json");
        var nanResp = await client.PutAsync("/boards/bad", content);
        Assert.Equal(HttpStatusCode.BadRequest, nanResp.StatusCode);
        Assert.Equal("error.board.elementBounds",
            (await nanResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    // ---- 3. Owned-image cleanup ----

    [Fact]
    public async Task Replacing_and_deleting_release_the_boards_images()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        var a = await UploadPngAsync(client);
        var b = await UploadPngAsync(client);

        (await client.PutAsJsonAsync("/boards/gallery", new { name = "G", elements = new object[] {
            new { kind = "image", x = 1.0, y = 1.0, w = 10.0, h = 10.0, image = a } } })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/images/{a}")).StatusCode);

        // Replace A with B → A's file is dropped, B's exists.
        (await client.PutAsJsonAsync("/boards/gallery", new { name = "G", elements = new object[] {
            new { kind = "image", x = 1.0, y = 1.0, w = 10.0, h = 10.0, image = b } } })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/images/{a}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/images/{b}")).StatusCode);

        // Deleting the board releases its remaining file.
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/boards/gallery")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/images/{b}")).StatusCode);
    }

    // ---- 4. Show flow: /tv/state render projection ----

    [Fact]
    public async Task Show_board_projects_a_render_model_and_defaults_the_label_to_the_name()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        var bg = await UploadPngAsync(client);
        var elImg = await UploadPngAsync(client);

        (await client.PutAsJsonAsync("/boards/dungeon", new
        {
            name = "Dungeon",
            backgroundColor = "#123456",
            backgroundImage = bg,
            elements = new object[]
            {
                new { kind = "image", x = 5.0, y = 5.0, w = 20.0, h = 20.0, image = elImg },
                new { kind = "text", x = 40.0, y = 60.0, w = 50.0, h = 15.0, text = "Beware", color = "#ff0000", size = 8.0, align = "right" },
            },
        })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/tv/show?board=dungeon")).StatusCode);

        var state = await client.GetFromJsonAsync<JsonElement>("/tv/state");
        var rev = state.GetProperty("rev").GetInt64();
        var content = state.GetProperty("content");
        Assert.Equal("board", content.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, content.GetProperty("url").ValueKind); // a board carries no stream url
        Assert.Equal("Dungeon", content.GetProperty("label").GetString());      // label defaulted to the name

        var board = content.GetProperty("board");
        Assert.Equal("#123456", board.GetProperty("backgroundColor").GetString());
        Assert.Equal($"/tv/content/board/{bg}?rev={rev}", board.GetProperty("backgroundUrl").GetString());

        var els = board.GetProperty("elements");
        Assert.Equal(2, els.GetArrayLength());
        Assert.Equal("image", els[0].GetProperty("kind").GetString());
        Assert.Equal($"/tv/content/board/{elImg}?rev={rev}", els[0].GetProperty("url").GetString());
        Assert.Equal(JsonValueKind.Null, els[0].GetProperty("text").ValueKind);
        Assert.Equal("text", els[1].GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, els[1].GetProperty("url").ValueKind);
        Assert.Equal("Beware", els[1].GetProperty("text").GetString());
        Assert.Equal("#FF0000", els[1].GetProperty("color").GetString());
        Assert.Equal(8.0, els[1].GetProperty("size").GetDouble());
        Assert.Equal("right", els[1].GetProperty("align").GetString());

        // The Recent entry records the board by (kind, ref) with the defaulted label.
        var recent = await client.GetFromJsonAsync<JsonElement>("/tv/show/recent");
        Assert.Equal("board", recent[0].GetProperty("kind").GetString());
        Assert.Equal("dungeon", recent[0].GetProperty("ref").GetString());
        Assert.Equal("Dungeon", recent[0].GetProperty("label").GetString());
    }

    // ---- 5. The key-free gate invariant ----

    [Fact]
    public async Task Board_image_route_serves_only_referenced_files_key_free()
    {
        using var factory = new ApiFactory(apiKey: Key);
        var client = factory.CreateClient();
        var referenced = await UploadPngAsync(client, apiKey: Key);
        var unreferenced = await UploadPngAsync(client, apiKey: Key);

        (await PutBoardAsync(client, "handout", new { name = "Handout", elements = new object[] {
            new { kind = "image", x = 1.0, y = 1.0, w = 50.0, h = 50.0, image = referenced } } }, apiKey: Key))
            .EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/tv/show?board=handout&apiKey={Key}")).StatusCode);

        // Key-free: the referenced file streams with its real content type.
        var served = await client.GetAsync($"/tv/content/board/{referenced}");
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal("image/png", served.Content.Headers.ContentType!.MediaType);

        // Key-free: an uploaded-but-UNreferenced image is NOT served through this route (membership gate),
        // even though it exists on disk — proving this is not a file-existence oracle.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/tv/content/board/{unreferenced}")).StatusCode);
        // …and a traversal name is 404, never a probe.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/tv/content/board/..%2Fappsettings.json")).StatusCode);
        // …while the general /images route stays locked for that same file.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/images/{unreferenced}")).StatusCode);

        // After clearing the display, even the previously-served file 404s (nothing is on the TV).
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/tv/clear?apiKey={Key}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/tv/content/board/{referenced}")).StatusCode);
    }

    // ---- 6. /tv/content/current is image-only ----

    [Fact]
    public async Task Content_current_404s_while_a_board_is_shown()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        (await client.PutAsJsonAsync("/boards/b1", new { name = "B1" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/tv/show?board=b1")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/tv/content/current")).StatusCode);
    }

    // ---- 7. Live edit reaches the TV via a rev bump ----

    [Fact]
    public async Task Editing_the_shown_board_bumps_rev_but_editing_another_does_not()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        (await client.PutAsJsonAsync("/boards/shown", new { name = "Shown" })).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync("/boards/other", new { name = "Other" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/tv/show?board=shown")).StatusCode);

        var rev1 = (await client.GetFromJsonAsync<JsonElement>("/tv/state")).GetProperty("rev").GetInt64();

        (await client.PutAsJsonAsync("/boards/shown", new { name = "Shown v2" })).EnsureSuccessStatusCode();
        var rev2 = (await client.GetFromJsonAsync<JsonElement>("/tv/state")).GetProperty("rev").GetInt64();
        Assert.True(rev2 > rev1, "editing the shown board should bump the rev");

        (await client.PutAsJsonAsync("/boards/other", new { name = "Other v2" })).EnsureSuccessStatusCode();
        var rev3 = (await client.GetFromJsonAsync<JsonElement>("/tv/state")).GetProperty("rev").GetInt64();
        Assert.Equal(rev2, rev3);
    }

    // ---- 8. Delete-while-shown scrubs the display and Recent ----

    [Fact]
    public async Task Deleting_the_shown_board_clears_the_display_and_recent()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        (await client.PutAsJsonAsync("/boards/live", new { name = "Live" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/tv/show?board=live")).StatusCode);
        var revBefore = (await client.GetFromJsonAsync<JsonElement>("/tv/state")).GetProperty("rev").GetInt64();

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/boards/live")).StatusCode);

        var state = await client.GetFromJsonAsync<JsonElement>("/tv/state");
        Assert.Equal(JsonValueKind.Null, state.GetProperty("content").ValueKind);
        Assert.True(state.GetProperty("rev").GetInt64() > revBefore, "deleting the shown board should bump the rev");

        var recent = await client.GetFromJsonAsync<JsonElement>("/tv/show/recent");
        Assert.DoesNotContain(recent.EnumerateArray(),
            r => r.GetProperty("kind").GetString() == "board" && r.GetProperty("ref").GetString() == "live");
    }

    // ---- 9. Show target errors ----

    [Fact]
    public async Task Show_target_errors_are_400_with_stable_codes()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var missing = await client.GetAsync("/tv/show?board=nope");
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("error.tv.boardNotFound",
            (await missing.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var both = await client.GetAsync("/tv/show?image=x.png&board=y");
        Assert.Equal(HttpStatusCode.BadRequest, both.StatusCode);
        Assert.Equal("error.tv.showTarget",
            (await both.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var neither = await client.GetAsync("/tv/show");
        Assert.Equal(HttpStatusCode.BadRequest, neither.StatusCode);
        Assert.Equal("error.tv.showTarget",
            (await neither.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    // ---- 10. Recent dedupe by (kind, ref) — an image and a board coexist ----

    [Fact]
    public async Task Recent_holds_an_image_and_a_board_deduped_by_kind_and_ref()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        var img = await UploadPngAsync(client);
        (await client.PutAsJsonAsync("/boards/board1", new { name = "Board1" })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/tv/show?image={img}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/tv/show?board=board1")).StatusCode);
        // Re-push the image — it moves to the front instead of duplicating, and the board stays in the list.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/tv/show?image={img}")).StatusCode);

        var recent = await client.GetFromJsonAsync<JsonElement>("/tv/show/recent");
        Assert.Equal(2, recent.GetArrayLength());
        Assert.Equal("image", recent[0].GetProperty("kind").GetString());
        Assert.Equal(img, recent[0].GetProperty("ref").GetString());
        Assert.Equal("board", recent[1].GetProperty("kind").GetString());
        Assert.Equal("board1", recent[1].GetProperty("ref").GetString());
    }
}
