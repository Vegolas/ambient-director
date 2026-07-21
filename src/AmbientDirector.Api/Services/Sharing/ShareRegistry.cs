using AmbientDirector.Api.Errors;

namespace AmbientDirector.Api.Services.Sharing;

/// <summary>Resolves a kind slug to its <see cref="IShareDescriptor"/> and defines the order commit upserts in
/// (dependencies before dependents), so a re-wired reference always points at an already-persisted row.</summary>
public sealed class ShareRegistry
{
    private readonly Dictionary<string, IShareDescriptor> _byKind;

    public ShareRegistry(IEnumerable<IShareDescriptor> descriptors) =>
        _byKind = descriptors.ToDictionary(d => d.Kind, StringComparer.OrdinalIgnoreCase);

    /// <summary>All registered kinds (for validating a manifest and enumerating an export/inspect).</summary>
    public IReadOnlyCollection<string> Kinds => _byKind.Keys;

    /// <summary>The descriptor for a kind, or <c>error.share.unknownKind</c> (400) for an unknown one.</summary>
    public IShareDescriptor Get(string kind) =>
        _byKind.TryGetValue(kind, out var descriptor)
            ? descriptor
            : throw new ValidationException("error.share.unknownKind", kind);

    public bool Has(string kind) => _byKind.ContainsKey(kind);

    /// <summary>A topological order of the cross-kind dependency DAG: a kind is listed after everything it can
    /// reference, so commit upserts leaf content (light FX, sounds, players, enemies) before the scenes/events/
    /// screens/encounters/boards that point at it.</summary>
    public static readonly string[] CommitOrder =
        ["lightfx", "sound", "player", "enemy", "scene", "event", "screen", "encounter", "board"];
}
