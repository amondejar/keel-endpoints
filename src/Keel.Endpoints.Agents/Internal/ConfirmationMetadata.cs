using System.Text.Json;

namespace Keel.Endpoints.Agents.Internal;

/// <summary>Added by <c>RequireConfirmation</c> (required) and <c>WithoutConfirmation</c> (not required).</summary>
internal sealed record ConfirmationMetadata(bool Required, Func<JsonElement, JsonSerializerOptions, string>? Message);
