using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.JSInterop;

namespace RpgSceneMaker.Ui.Services;

public record SceneDto(string Id, string Name, LightDto? Light, MusicDto? Music, List<string>? SoundEffects);
public record LightDto(bool? Power, string? Color, int? Brightness, int? Temperature);
public record MusicDto(string? PlayId, double? Volume, bool Pause);
public record ActivationDto(string Scene, string Light, string Music, string SoundEffects, bool FullySucceeded);
public record ActiveSceneDto(string? Id, DateTimeOffset? ActivatedAt);

public record KenkuItem(string Id, string Title);
public record KenkuGroup(string Id, string Title, List<KenkuItem> Items);
public record MusicState(bool Playing, double Volume, bool Muted, bool Shuffle, string Repeat,
    string? TrackTitle, string? PlaylistTitle, double Progress, double Duration);

/// <summary>All communication with the Scene Maker API, with the optional API key attached.</summary>
public class ApiClient(HttpClient http, IJSRuntime js, UiState ui)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private string? _apiKey;
    private bool _keyLoaded;

    // ---------- api key (persisted in the browser) ----------

    public async Task<string?> GetApiKeyAsync()
    {
        if (!_keyLoaded)
        {
            _apiKey = await js.InvokeAsync<string?>("localStorage.getItem", "apiKey");
            _keyLoaded = true;
        }
        return _apiKey;
    }

    public async Task SetApiKeyAsync(string? key)
    {
        _apiKey = string.IsNullOrWhiteSpace(key) ? null : key.Trim();
        _keyLoaded = true;
        if (_apiKey is null)
            await js.InvokeVoidAsync("localStorage.removeItem", "apiKey");
        else
            await js.InvokeVoidAsync("localStorage.setItem", "apiKey", _apiKey);
    }

    // ---------- scenes ----------

    public async Task<List<SceneDto>> GetScenesAsync() =>
        await GetAsync<List<SceneDto>>("scenes") ?? [];

    public Task<ActiveSceneDto?> GetActiveSceneAsync() => GetAsync<ActiveSceneDto?>("scenes/active");

    public async Task<ActivationDto?> ActivateSceneAsync(string id)
    {
        try
        {
            using var response = await SendAsync(HttpMethod.Post, $"scenes/{id}/activate");
            var result = await response.Content.ReadFromJsonAsync<ActivationDto>(Json);
            ui.SetConnected(true);
            return result;
        }
        catch (Exception ex)
        {
            ReportTransportError(ex);
            return null;
        }
    }

    // ---------- fire-and-forget commands (lights, music, sfx) ----------

    public async Task<bool> CommandAsync(string path, string? okMessage = null)
    {
        try
        {
            using var response = await SendAsync(HttpMethod.Post, path);
            ui.SetConnected(true);
            if (!response.IsSuccessStatusCode)
            {
                ui.ReportError(await ExtractProblemAsync(response));
                return false;
            }
            if (okMessage is not null) ui.ReportOk(okMessage);
            return true;
        }
        catch (Exception ex)
        {
            ReportTransportError(ex);
            return false;
        }
    }

    // ---------- kenku data ----------

    public async Task<List<KenkuGroup>> GetPlaylistsAsync() =>
        ParseGroups(await GetNodeAsync("music/playlists"), "playlists", "tracks");

    public async Task<List<KenkuGroup>> GetSoundboardsAsync() =>
        ParseGroups(await GetNodeAsync("sfx/sounds"), "soundboards", "sounds");

    public async Task<MusicState?> GetMusicStateAsync()
    {
        var node = await GetNodeAsync("music/state");
        if (node is null) return null;

        var track = node["track"];
        var playlist = node["playlist"];
        return new MusicState(
            Playing: node["playing"]?.GetValue<bool>() ?? false,
            Volume: node["volume"]?.GetValue<double>() ?? 0.5,
            Muted: node["muted"]?.GetValue<bool>() ?? false,
            Shuffle: node["shuffle"]?.GetValue<bool>() ?? false,
            Repeat: node["repeat"]?.GetValue<string>() ?? "playlist",
            TrackTitle: track?["title"]?.GetValue<string>(),
            PlaylistTitle: playlist?["title"]?.GetValue<string>(),
            Progress: track?["progress"]?.GetValue<double>() ?? 0,
            Duration: track?["duration"]?.GetValue<double>() ?? 0);
    }

    public async Task<HashSet<string>> GetPlayingSoundIdsAsync()
    {
        var node = await GetNodeAsync("sfx/state");
        var ids = new HashSet<string>();
        if (node?["sounds"] is JsonArray sounds)
            foreach (var sound in sounds)
                if (sound?["id"]?.GetValue<string>() is { } id)
                    ids.Add(id);
        return ids;
    }

    // ---------- plumbing ----------

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        if (await GetApiKeyAsync() is { } key)
            request.Headers.Add("X-Api-Key", key);
        return await http.SendAsync(request);
    }

    /// <summary>GET with silent failure — pollers use this so a hiccup only flips the connection dot.</summary>
    private async Task<T?> GetAsync<T>(string path)
    {
        try
        {
            using var response = await SendAsync(HttpMethod.Get, path);
            ui.SetConnected(true);
            if (!response.IsSuccessStatusCode) return default;
            return await response.Content.ReadFromJsonAsync<T>(Json);
        }
        catch (Exception)
        {
            ui.SetConnected(false);
            return default;
        }
    }

    private async Task<JsonNode?> GetNodeAsync(string path)
    {
        try
        {
            using var response = await SendAsync(HttpMethod.Get, path);
            ui.SetConnected(true);
            if (!response.IsSuccessStatusCode) return null;
            return JsonNode.Parse(await response.Content.ReadAsStringAsync());
        }
        catch (Exception)
        {
            ui.SetConnected(false);
            return null;
        }
    }

    private static List<KenkuGroup> ParseGroups(JsonNode? node, string groupsKey, string itemsKey)
    {
        var groups = new List<KenkuGroup>();
        if (node is null) return groups;

        // Kenku returns { "<groupsKey>": [{id,title,<itemsKey>:[ids]}], "<itemsKey>": [{id,title,...}] }
        var itemLookup = new Dictionary<string, KenkuItem>();
        if (node[itemsKey] is JsonArray allItems)
            foreach (var item in allItems)
                if (item?["id"]?.GetValue<string>() is { } id)
                    itemLookup[id] = new KenkuItem(id, item["title"]?.GetValue<string>() ?? id);

        if (node[groupsKey] is JsonArray groupArray)
        {
            foreach (var group in groupArray)
            {
                if (group?["id"]?.GetValue<string>() is not { } groupId) continue;
                var items = new List<KenkuItem>();
                if (group[itemsKey] is JsonArray memberIds)
                    foreach (var memberId in memberIds)
                        if (memberId?.GetValue<string>() is { } mid && itemLookup.TryGetValue(mid, out var item))
                            items.Add(item);
                groups.Add(new KenkuGroup(groupId, group["title"]?.GetValue<string>() ?? groupId, items));
            }
        }
        return groups;
    }

    private static async Task<string> ExtractProblemAsync(HttpResponseMessage response)
    {
        try
        {
            var node = JsonNode.Parse(await response.Content.ReadAsStringAsync());
            var title = node?["title"]?.GetValue<string>();
            var detail = node?["detail"]?.GetValue<string>();
            return (title, detail) switch
            {
                (not null, not null) => $"{title}: {detail}",
                (not null, null) => title,
                _ => $"HTTP {(int)response.StatusCode}",
            };
        }
        catch
        {
            return $"HTTP {(int)response.StatusCode}";
        }
    }

    private void ReportTransportError(Exception ex)
    {
        ui.SetConnected(false);
        ui.ReportError($"API unreachable: {ex.Message}");
    }
}
