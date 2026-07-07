using RpgSceneMaker.Api.Models;

namespace RpgSceneMaker.Api.Services;

public record ActivationResult(string Scene, string Light, string Music, string SoundEffects)
{
    public bool FullySucceeded => Light is "ok" or "skipped" && Music is "ok" or "skipped" && SoundEffects is "ok" or "skipped";
}

/// <summary>Applies a scene: light, music and sound effects run concurrently so the table switches fast.</summary>
public class SceneActivator(ILightService lights, KenkuClient kenku, ILogger<SceneActivator> logger)
{
    public async Task<ActivationResult> ActivateAsync(Scene scene)
    {
        var lightTask = RunAsync("light", () => scene.Light is null
            ? Task.FromResult(false)
            : Apply(() => lights.ApplyAsync(scene.Light)));

        var musicTask = RunAsync("music", () => ApplyMusicAsync(scene.Music));

        var sfxTask = RunAsync("sfx", () => ApplySoundEffectsAsync(scene.SoundEffects));

        await Task.WhenAll(lightTask, musicTask, sfxTask);
        return new ActivationResult(scene.Id, lightTask.Result, musicTask.Result, sfxTask.Result);
    }

    private static async Task<bool> Apply(Func<Task> action)
    {
        await action();
        return true;
    }

    private async Task<bool> ApplyMusicAsync(MusicSettings? music)
    {
        if (music is null) return false;

        var didSomething = false;
        if (music.Volume is double volume)
        {
            await kenku.SetVolumeAsync(volume);
            didSomething = true;
        }
        if (music.Pause)
        {
            await kenku.PauseAsync();
            didSomething = true;
        }
        else if (!string.IsNullOrWhiteSpace(music.PlayId))
        {
            await kenku.PlayAsync(music.PlayId);
            didSomething = true;
        }
        return didSomething;
    }

    private async Task<bool> ApplySoundEffectsAsync(List<string> soundIds)
    {
        if (soundIds.Count == 0) return false;
        foreach (var id in soundIds.Where(id => !string.IsNullOrWhiteSpace(id)))
            await kenku.PlaySoundAsync(id);
        return true;
    }

    private async Task<string> RunAsync(string part, Func<Task<bool>> action)
    {
        try
        {
            return await action() ? "ok" : "skipped";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Scene activation: {Part} failed", part);
            return $"error: {ex.Message}";
        }
    }
}
