namespace Keel.Endpoints.Agents;

/// <summary>Where an approval stands.</summary>
public enum AgentApprovalStatus
{
    /// <summary>Waiting for approvers.</summary>
    Pending,

    /// <summary>Approved by enough people: the agent's next identical call runs the tool, once.</summary>
    Approved,

    /// <summary>Rejected by an approver: the tool does not run.</summary>
    Rejected,
}

/// <summary>
/// A request, made by an agent's call, for people to approve a tool call before it runs. Bound to the tool, a hash of
/// the arguments and the caller: approving it allows exactly that call, once.
/// </summary>
public sealed record AgentApproval
{
    /// <summary>A random identifier, also the secret part of the approval link.</summary>
    public required string Id { get; init; }

    /// <summary>The tool name.</summary>
    public required string Tool { get; init; }

    /// <summary>SHA-256 of the call's arguments.</summary>
    public required string ArgumentsHash { get; init; }

    /// <summary>The caller whose agent made the call, or <see langword="null"/> when anonymous.</summary>
    public string? RequestedBy { get; init; }

    /// <summary>What the approvers are asked to approve.</summary>
    public required string Message { get; init; }

    /// <summary>When the approval was requested.</summary>
    public required DateTimeOffset Created { get; init; }

    /// <summary>After this, it can no longer be approved or used.</summary>
    public required DateTimeOffset Expires { get; init; }

    /// <summary>How many different people must approve.</summary>
    public int RequiredApprovals { get; init; } = 1;

    /// <summary>The authorization policy an approver must satisfy, if any.</summary>
    public string? Policy { get; init; }

    /// <summary>Whether the caller may approve their own request.</summary>
    public bool RequesterMayApprove { get; init; }

    /// <summary>Only the caller may approve (a confirmation the MCP client could not show).</summary>
    public bool RequesterOnly { get; init; }

    /// <summary>Who approved so far.</summary>
    public IReadOnlyList<string> ApprovedBy { get; init; } = [];

    /// <summary>Who rejected it, if someone did.</summary>
    public string? RejectedBy { get; init; }

    /// <summary>Where it stands.</summary>
    public AgentApprovalStatus Status { get; init; }

    /// <summary>The agent has been told the outcome: it ran (approved) or was told it was rejected. Closed for good.</summary>
    public bool Consumed { get; init; }

    /// <summary>Incremented on every change, for optimistic concurrency in <see cref="IAgentApprovalStore.TryReplaceAsync"/>.</summary>
    public int Version { get; init; }
}

/// <summary>
/// Where approvals live. The default keeps them in memory, which is enough for one instance; register your own (a
/// database table) when the app runs on several instances or approvals must survive a restart.
/// </summary>
public interface IAgentApprovalStore
{
    /// <summary>The approval with this id, or <see langword="null"/>.</summary>
    ValueTask<AgentApproval?> GetAsync(string id, CancellationToken cancellationToken);

    /// <summary>The most recent approval not yet <see cref="AgentApproval.Consumed"/> for this tool, arguments and caller.</summary>
    ValueTask<AgentApproval?> FindOpenAsync(string tool, string argumentsHash, string? requestedBy, CancellationToken cancellationToken);

    /// <summary>Stores a new approval.</summary>
    ValueTask AddAsync(AgentApproval approval, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces <paramref name="current"/> with <paramref name="updated"/> only if the stored version is still
    /// <paramref name="current"/>'s: returns <see langword="false"/> when someone else changed it first.
    /// </summary>
    ValueTask<bool> TryReplaceAsync(AgentApproval current, AgentApproval updated, CancellationToken cancellationToken);
}
