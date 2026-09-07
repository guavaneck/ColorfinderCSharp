using System.Text.Json;

namespace ColorFinder.Desktop.Interop;

public sealed record BridgeRequest(string Id, string Action, JsonElement Payload);
public sealed record BridgeResponse(string Id, bool Ok, object? Result = null, string? Error = null);
