using System.ComponentModel.DataAnnotations.Schema;

namespace RpgSceneMaker.Api.Data;

/// <summary>
/// Anthropic (bring-your-own-key) assistant settings, stored as a single row (Id = 1) in SQLite.
/// The API key comes from console.anthropic.com; it powers the in-panel chat assistant and is never
/// returned by any endpoint once saved.
/// </summary>
public class AnthropicConfig
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    /// <summary>API key from console.anthropic.com. Empty until the user configures it.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Model id the assistant talks to (see console.anthropic.com for available models).</summary>
    public string Model { get; set; } = "claude-opus-4-8";

    [NotMapped]
    public bool IsConfigured => ApiKey != "";
}
