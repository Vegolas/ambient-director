namespace RpgSceneMaker.Api.Contracts;

// Body of PUT /setup/anthropic/config (both optional — an empty ApiKey keeps the saved key so the model
// can change without re-pasting it; the key is never echoed back).
public record AnthropicConfigInput(string? ApiKey, string? Model);
