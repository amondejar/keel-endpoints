using System.Text.Json;

namespace Keel.Endpoints.Agents.Internal;

/// <summary>Added by <c>RequireConfirmation</c> (required) and <c>WithoutConfirmation</c> (not required).</summary>
internal sealed record ConfirmationMetadata(bool Required, Func<JsonElement, JsonSerializerOptions, string>? Message);

/// <summary>Added by <c>RequireApproval</c>.</summary>
internal sealed record ApprovalMetadata(Func<JsonElement, JsonSerializerOptions, string> Message, AgentApprovalOptions Options);
