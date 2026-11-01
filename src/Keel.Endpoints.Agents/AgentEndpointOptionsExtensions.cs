using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Keel.Endpoints.Agents;
using Keel.Endpoints.Agents.Internal;
using Microsoft.AspNetCore.Builder;

namespace Keel.Endpoints;

/// <summary>Confirmation or approval of an endpoint's MCP tool calls, in its <c>Configure</c>.</summary>
public static class AgentEndpointOptionsExtensions
{
    /// <summary>
    /// Before an agent's call to this endpoint's tool runs, the server asks the user to confirm, showing
    /// <paramref name="message"/> (requires <c>AddKeelAgents()</c>). Without a confirmation the endpoint does not run.
    /// </summary>
    public static EndpointOptions RequireConfirmation(this EndpointOptions options, string message)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return options.Convention(b => b.WithMetadata(new ConfirmationMetadata(true, (_, _) => message)));
    }

    /// <summary>
    /// Like <see cref="RequireConfirmation(EndpointOptions, string)"/>, with a message built from the call's arguments,
    /// so the user sees what will happen: <c>r =&gt; $"Reject loan application {r.Id} ({r.Amount} €)"</c>.
    /// </summary>
    /// <typeparam name="TRequest">The endpoint's request contract; the arguments are read as it, with the application's JSON options.</typeparam>
    public static EndpointOptions RequireConfirmation<TRequest>(this EndpointOptions options, Func<TRequest, string> message)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(message);
        return options.Convention(b => b.WithMetadata(new ConfirmationMetadata(true, Render(message))));
    }

    /// <summary>
    /// Before an agent's call to this endpoint's tool runs, people approve it through a link (requires
    /// <c>AddKeelAgents()</c> and <c>MapKeelApprovals()</c>). The agent gets the link to pass on; once approved, its next
    /// call with the same arguments runs the endpoint, once. By default one person other than the caller approves.
    /// </summary>
    public static EndpointOptions RequireApproval(this EndpointOptions options, string message, Action<AgentApprovalOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return options.Convention(b => b.WithMetadata(new ApprovalMetadata((_, _) => message, Approval(configure))));
    }

    /// <summary>
    /// Like <see cref="RequireApproval(EndpointOptions, string, Action{AgentApprovalOptions}?)"/>, with a message built
    /// from the call's arguments, so approvers see what they approve.
    /// </summary>
    /// <typeparam name="TRequest">The endpoint's request contract; the arguments are read as it, with the application's JSON options.</typeparam>
    public static EndpointOptions RequireApproval<TRequest>(this EndpointOptions options, Func<TRequest, string> message, Action<AgentApprovalOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(message);
        return options.Convention(b => b.WithMetadata(new ApprovalMetadata(Render(message), Approval(configure))));
    }

    private static AgentApprovalOptions Approval(Action<AgentApprovalOptions>? configure)
    {
        var approval = new AgentApprovalOptions();
        configure?.Invoke(approval);
        if (approval.Approvals < 1) throw new ArgumentOutOfRangeException(nameof(configure), "Approvals must be at least 1.");
        return approval;
    }

    private static Func<JsonElement, JsonSerializerOptions, string> Render<TRequest>(Func<TRequest, string> message) => (arguments, json) =>
    {
        var info = (JsonTypeInfo<TRequest>)json.GetTypeInfo(typeof(TRequest));
        var request = arguments.Deserialize(info);
        return request is null ? throw new JsonException("The tool arguments are empty.") : message(request);
    };

    /// <summary>
    /// The tool runs without asking the user, even if it is destructive. For tools whose effect is easy to undo; the
    /// call is still audited.
    /// </summary>
    public static EndpointOptions WithoutConfirmation(this EndpointOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Convention(b => b.WithMetadata(new ConfirmationMetadata(false, null)));
    }
}
