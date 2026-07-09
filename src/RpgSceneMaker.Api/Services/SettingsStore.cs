using System.Text.Json;
using System.Text.Json.Nodes;

namespace RpgSceneMaker.Api.Services;

public record TuyaConfigDto(string Ip, string DeviceId, string LocalKey, string ProtocolVersion, string DpProfile);
public record HueConfigDto(string BridgeIp, string AppKey, List<string> LightIds);
public record LightingConfigDto(string Provider, HueConfigDto Hue, TuyaConfigDto Tuya);

/// <summary>
/// Persists settings changed from the UI into settings.local.json, which is registered
/// as a reloadOnChange config source layered over appsettings.json — so saves apply
/// immediately without a restart and never touch the committed appsettings.json.
/// </summary>
public class SettingsStore(IHostEnvironment env)
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public const string FileName = "settings.local.json";

    private readonly Lock _lock = new();

    private string FilePath => Path.Combine(env.ContentRootPath, FileName);

    public void Save(LightingConfigDto config)
    {
        lock (_lock)
        {
            var root = File.Exists(FilePath)
                ? JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject ?? new JsonObject()
                : new JsonObject();

            root["Lighting"] = new JsonObject
            {
                ["Provider"] = config.Provider,
            };
            root["Hue"] = new JsonObject
            {
                ["BridgeIp"] = config.Hue.BridgeIp,
                ["AppKey"] = config.Hue.AppKey,
                ["LightIds"] = new JsonArray([.. config.Hue.LightIds.Select(id => JsonValue.Create(id))]),
            };
            root["Tuya"] = new JsonObject
            {
                ["Ip"] = config.Tuya.Ip,
                ["DeviceId"] = config.Tuya.DeviceId,
                ["LocalKey"] = config.Tuya.LocalKey,
                ["ProtocolVersion"] = config.Tuya.ProtocolVersion,
                ["DpProfile"] = config.Tuya.DpProfile,
            };

            File.WriteAllText(FilePath, root.ToJsonString(Indented));
        }
    }
}
