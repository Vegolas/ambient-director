using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace RpgSceneMaker.Api.Services;

/// <summary>
/// Thin client for the Kenku FM remote API (Kenku FM > Settings > Remote, default 127.0.0.1:3333).
/// </summary>
public class KenkuClient(HttpClient http, IOptionsMonitor<KenkuOptions> options)
{
    // Playlists (background music)
    public Task<JsonElement> GetPlaylistsAsync() => GetAsync("/v1/playlist");
    public Task<JsonElement> GetPlaylistStateAsync() => GetAsync("/v1/playlist/playback");
    public Task PlayAsync(string id) => SendAsync(HttpMethod.Put, "/v1/playlist/play", new { id });
    public Task PauseAsync() => SendAsync(HttpMethod.Put, "/v1/playlist/playback/pause");
    public Task ResumeAsync() => SendAsync(HttpMethod.Put, "/v1/playlist/playback/play");
    public Task NextAsync() => SendAsync(HttpMethod.Post, "/v1/playlist/playback/next");
    public Task PreviousAsync() => SendAsync(HttpMethod.Post, "/v1/playlist/playback/previous");
    public Task SetVolumeAsync(double volume) => SendAsync(HttpMethod.Put, "/v1/playlist/playback/volume", new { volume = Math.Clamp(volume, 0, 1) });
    public Task SetMuteAsync(bool mute) => SendAsync(HttpMethod.Put, "/v1/playlist/playback/mute", new { mute });
    public Task SetShuffleAsync(bool shuffle) => SendAsync(HttpMethod.Put, "/v1/playlist/playback/shuffle", new { shuffle });
    public Task SetRepeatAsync(string mode) => SendAsync(HttpMethod.Put, "/v1/playlist/playback/repeat", new { repeat = mode }); // off | track | playlist

    // Soundboard (one-shot effects)
    public Task<JsonElement> GetSoundboardsAsync() => GetAsync("/v1/soundboard");
    public Task<JsonElement> GetSoundboardStateAsync() => GetAsync("/v1/soundboard/playback");
    public Task PlaySoundAsync(string id) => SendAsync(HttpMethod.Put, "/v1/soundboard/play", new { id });
    public Task StopSoundAsync(string id) => SendAsync(HttpMethod.Put, "/v1/soundboard/stop", new { id });

    private Uri BuildUri(string path) => new(options.CurrentValue.BaseUrl.TrimEnd('/') + path);

    private async Task<JsonElement> GetAsync(string path)
    {
        using var response = await http.GetAsync(BuildUri(path));
        await EnsureSuccessAsync(response, path);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return doc.RootElement.Clone();
    }

    private async Task SendAsync(HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, BuildUri(path));
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request);
        await EnsureSuccessAsync(response, path);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string path)
    {
        if (response.IsSuccessStatusCode) return;
        var detail = await response.Content.ReadAsStringAsync();
        throw new KenkuException($"Kenku FM returned {(int)response.StatusCode} for {path}: {detail}");
    }
}

public class KenkuException(string message) : Exception(message);
