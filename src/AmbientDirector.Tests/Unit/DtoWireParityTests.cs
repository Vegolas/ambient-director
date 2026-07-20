using System.Buffers;
using System.Linq;
using System.Text;
using System.Text.Json;
using AmbientDirector.Api.Models;
using AmbientDirector.Ui.Contracts;
using Xunit;

namespace AmbientDirector.Tests.Unit;

/// <summary>
/// Four entities are "model-is-wire": the API serializes <see cref="Scene"/>/<see cref="GameEvent"/>/
/// <see cref="Screen"/>/<see cref="LightFx"/> straight onto their endpoints with no <c>Contracts/</c> DTO, and
/// the Blazor panel mirrors each shape by hand in its own <c>Contracts/</c> (<see cref="SceneDto"/>/
/// <see cref="EventDto"/>/<see cref="ScreenDto"/>/<see cref="LightFxDto"/>). Nothing but discipline keeps the
/// two copies in sync — the #74 audit (PR #93) found them aligned but unguarded, so this test (issue #95) is
/// the guard.
///
/// Each case builds a fully-populated API instance, serializes it with the wire's
/// <see cref="JsonSerializerDefaults.Web"/> options, deserializes into the UI DTO and back into the API model,
/// and asserts the JSON is structurally identical at every hop (object-property order ignored, array order
/// kept — the UI records serialize fields in constructor order, e.g. <see cref="SceneLightDto"/> lists Color
/// after Brightness). A field added to one side but not the other, a rename, or a value that fails to bind
/// through the DTO changes one of the snapshots and fails the build. Because the four are all accepted by their
/// PUT endpoints, the reverse binding (UI DTO -> API model) is checked too.
/// </summary>
public class DtoWireParityTests
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Scene_round_trips_through_the_UI_dto()
    {
        var scene = new Scene
        {
            Id = "tavern",
            Name = "The Prancing Pony",
            Light = new LightSettings { Power = true, Color = "#FF8C2A", Brightness = 80, Temperature = 40 },
            Lights =
            [
                new SceneLight
                {
                    LightKey = "hue-1",
                    Power = true,
                    Color = "#123456",
                    Brightness = 55,
                    Temperature = 30,
                    Effect = SampleEffect(),
                },
            ],
            Music = new MusicSettings { Source = "spotify", PlayId = "spotify:track:abc", Volume = 0.5, Pause = true },
            SoundEffects = ["fire", "crowd"],
            Image = "tavern.png",
        };

        AssertWireParity<Scene, SceneDto>(scene);
    }

    [Fact]
    public void Event_round_trips_through_the_UI_dto()
    {
        var gameEvent = new GameEvent
        {
            Id = "thunder",
            Name = "Thunderclap",
            Flash = new EventFlash { Color = "#FFFFFF", Brightness = 100, DurationMs = 200 },
            SoundEffects = ["thunder"],
            Image = "storm.png",
            Timeline = new EventTimeline
            {
                Sounds = [new TimelineSoundClip { SoundId = "thunder", StartMs = 0, DurationMs = 1500, Volume = 0.7 }],
                Lights =
                [
                    new TimelineLightClip
                    {
                        LightKey = "hue-1",
                        StartMs = 100,
                        DurationMs = 1000,
                        Power = true,
                        Color = "#FF0000",
                        Brightness = 90,
                        Temperature = 25,
                        Effect = SampleEffect(),
                    },
                ],
            },
            After = new EventAfter { Mode = "scene", SceneId = "tavern" },
        };

        AssertWireParity<GameEvent, EventDto>(gameEvent);
    }

    [Fact]
    public void Screen_round_trips_through_the_UI_dto()
    {
        var screen = new Screen
        {
            Id = "fantasy",
            Name = "Fantasy",
            Tiles =
            [
                new ScreenTile { Kind = "scene", Ref = "tavern", Label = "Tavern" },
                new ScreenTile { Kind = "music", Ref = "spotify:playlist:xyz", Label = "Battle" },
            ],
            Image = "board.png",
            Compact = true,
        };

        AssertWireParity<Screen, ScreenDto>(screen);
    }

    [Fact]
    public void LightFx_round_trips_through_the_UI_dto()
    {
        var lightFx = new LightFx
        {
            Id = "candle",
            Name = "Candle flicker",
            Keyframes = [SampleKeyframe(), SampleKeyframe(200)],
            Loop = true,
            CycleMs = 5000,
        };

        AssertWireParity<LightFx, LightFxDto>(lightFx);
    }

    // A fully-populated fx effect, shared by the scene and event fixtures. Every field carries a distinctive
    // non-default value so a member the UI DTO silently drops (or fails to carry a value through) surfaces as a
    // JSON difference rather than passing because both sides happen to hold the same default.
    private static LightEffect SampleEffect() => new()
    {
        Type = "fx",
        FxId = "candle",
        Speed = 7,
        Intensity = 9,
        Colors = ["#111111", "#222222"],
        Keyframes = [SampleKeyframe()],
        Loop = true,
        CycleMs = 4000,
    };

    private static LightKeyframe SampleKeyframe(int atMs = 100) => new()
    {
        AtMs = atMs,
        Power = true,
        Color = "#ABCDEF",
        Brightness = 60,
        Temperature = 20,
        TransitionMs = 400,
    };

    // Round-trips API -> json -> UI DTO -> json -> API and asserts every hop serializes to structurally
    // identical JSON. The first assertion catches a member on one side but not the other (Web serialization
    // writes nulls, so even an unpopulated dropped field shows up as a differing key set); the second catches a
    // member that serializes out of the API model but no longer binds back into it.
    private static void AssertWireParity<TApi, TUi>(TApi apiObj)
    {
        var apiJson = JsonSerializer.Serialize(apiObj, Wire);

        var uiObj = JsonSerializer.Deserialize<TUi>(apiJson, Wire);
        var uiJson = JsonSerializer.Serialize(uiObj, Wire);
        Assert.Equal(Canonical(apiJson), Canonical(uiJson));

        var apiObj2 = JsonSerializer.Deserialize<TApi>(uiJson, Wire);
        var apiJson2 = JsonSerializer.Serialize(apiObj2, Wire);
        Assert.Equal(Canonical(apiJson), Canonical(apiJson2));
    }

    // Re-emits JSON with object keys sorted recursively (array order preserved) and indented, so the equality
    // assertions ignore property order while xUnit still prints a readable diff on failure.
    private static string Canonical(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            WriteSorted(doc.RootElement, writer);
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteSorted(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteSorted(property.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteSorted(item, writer);
                }
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
