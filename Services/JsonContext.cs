using System.Text.Json.Serialization;
using ClaudeSessionManager.Models;

namespace ClaudeSessionManager.Services;

[JsonSerializable(typeof(ClaudeSession))]
[JsonSerializable(typeof(SessionsIndex))]
[JsonSerializable(typeof(PromotedSessionsStore))]
[JsonSerializable(typeof(PromotedMetadata))]
[JsonSerializable(typeof(Note))]
[JsonSerializable(typeof(SessionStatus))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true)]
public partial class JsonContext : JsonSerializerContext
{
}
