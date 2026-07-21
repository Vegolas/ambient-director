using AmbientDirector.Api.Models;
using AmbientDirector.Api.Validation;

namespace AmbientDirector.Api.Services.Sharing;

/// <summary>A soundboard sound: its audio file (and optional tile art) travel in the pack. Import recreates the
/// files under fresh names and keeps ALL of the sound's metadata (duration, waveform, attribution) — it must
/// NOT go through <c>SoundImporter</c>, which re-measures and would discard the bundled values.</summary>
public sealed class SoundShareDescriptor(SoundStore store) : ShareDescriptor<Sound>
{
    public override string Kind => "sound";
    protected override Task<Sound?> GetAsync(string id) => store.GetAsync(id);
    protected override Task UpsertAsync(Sound sound) => store.UpsertAsync(sound);
    protected override void Validate(Sound sound) => SoundValidation.Validate(sound);
    protected override string GetId(Sound sound) => sound.Id;
    protected override void SetId(Sound sound, string id) => sound.Id = id;
    protected override string GetName(Sound sound) => sound.Name;

    protected override IEnumerable<MediaRef> Media(Sound sound)
    {
        if (!string.IsNullOrEmpty(sound.FileName)) yield return new(MediaKind.Audio, sound.FileName, Required: true);
        if (!string.IsNullOrEmpty(sound.Image)) yield return new(MediaKind.Image, sound.Image!);
    }

    protected override void Rewrite(Sound sound, ShareRewriteContext ctx)
    {
        // Only the file names change; duration/waveform/attribution are preserved verbatim. The importer has
        // already guaranteed the audio file is present (else error.share.mediaMissing), so this maps cleanly.
        var audio = ctx.MapMedia(sound.FileName);
        if (audio is not null) sound.FileName = audio;
        sound.Image = ctx.MapMedia(sound.Image);
    }
}
