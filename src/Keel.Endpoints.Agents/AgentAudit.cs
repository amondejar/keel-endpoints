namespace Keel.Endpoints.Agents;

/// <summary>What happened to an agent's call to a tool that changes data.</summary>
public enum AgentDecision
{
    /// <summary>No confirmation was needed: the tool ran.</summary>
    NotRequired,

    /// <summary>The user was asked to confirm; the tool has not run yet.</summary>
    ConfirmationRequested,

    /// <summary>The user confirmed: the tool ran.</summary>
    Confirmed,

    /// <summary>The user declined or cancelled: the tool did not run.</summary>
    Declined,

    /// <summary>A confirmation was needed and could not be obtained (the client cannot ask, or the answer was not valid for this call): the tool did not run.</summary>
    Refused,

    /// <summary>An approval was requested: the agent got a link; the tool has not run.</summary>
    ApprovalRequested,

    /// <summary>An approver approved (the entry's user is the approver). The tool runs once enough have.</summary>
    ApprovalGranted,

    /// <summary>An approver rejected (the entry's user is the approver): the tool will not run.</summary>
    ApprovalRejected,

    /// <summary>The approved call ran.</summary>
    Approved,
}

/// <summary>One entry of the audit trail.</summary>
/// <param name="Time">When it happened.</param>
/// <param name="Tool">The tool name.</param>
/// <param name="User">The caller's identifier (<c>NameIdentifier</c> or <c>Name</c> claim), or <see langword="null"/> when anonymous.</param>
/// <param name="Decision">What was decided.</param>
/// <param name="Summary">The confirmation message shown to the user, if one was needed.</param>
/// <param name="ArgumentsHash">A SHA-256 of the tool arguments: correlates the entries of one call without storing its data.</param>
/// <param name="Succeeded">For a tool that ran: whether it answered with success. <see langword="null"/> when it did not run.</param>
public sealed record AgentAuditEntry(
    DateTimeOffset Time,
    string Tool,
    string? User,
    AgentDecision Decision,
    string? Summary,
    string ArgumentsHash,
    bool? Succeeded);

/// <summary>
/// Receives the audit trail: every call an agent makes to a tool that changes data, and every confirmation. The
/// default writes them to the log (<c>Keel.Endpoints.Agents</c> category); register your own to keep them in a store.
/// </summary>
public interface IAgentAuditSink
{
    /// <summary>Records an entry. Called on the request path: keep it fast, or queue the work.</summary>
    ValueTask WriteAsync(AgentAuditEntry entry, CancellationToken cancellationToken);
}
