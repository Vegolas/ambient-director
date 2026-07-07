using RpgSceneMaker.Api.Models;

namespace RpgSceneMaker.Api.Services;

/// <summary>A scene-controllable light. Implemented by TuyaLightService and HueLightService.</summary>
public interface ILightService
{
    Task SetPowerAsync(bool on);

    /// <summary>Flips power and returns the new state.</summary>
    Task<bool> ToggleAsync();

    /// <summary>Colour mode. Brightness percent (0-100) overrides the value baked into the hex color.</summary>
    Task SetColorAsync(string hexColor, int? brightnessPercent = null);

    /// <summary>White mode. Temperature 0 (warm) - 100 (cold).</summary>
    Task SetWhiteAsync(int brightnessPercent, int? temperaturePercent = null);

    /// <summary>Set brightness (0-100) without changing mode or hue.</summary>
    Task SetBrightnessAsync(int percent);

    /// <summary>Raw provider-specific state, for diagnostics.</summary>
    Task<object> GetStatusAsync();

    /// <summary>Apply a scene's light settings.</summary>
    async Task ApplyAsync(LightSettings light)
    {
        if (light.Power == false)
        {
            await SetPowerAsync(false);
            return;
        }

        if (!string.IsNullOrWhiteSpace(light.Color))
            await SetColorAsync(light.Color, light.Brightness);
        else if (light.Brightness is not null || light.Temperature is not null)
            await SetWhiteAsync(light.Brightness ?? 100, light.Temperature);
        else if (light.Power == true)
            await SetPowerAsync(true);
    }
}
