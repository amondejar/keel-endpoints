namespace Keel.Endpoints.Agents;

/// <summary>Who approves an endpoint's tool calls (<c>RequireApproval</c>).</summary>
public sealed class AgentApprovalOptions
{
    /// <summary>How many different people must approve. Default 1; more than 1 needs authenticated approvers.</summary>
    public int Approvals { get; set; } = 1;

    /// <summary>The authorization policy an approver must satisfy (<c>"supervisors"</c>). Default: anyone with the link.</summary>
    public string? Policy { get; set; }

    /// <summary>
    /// Whether the caller may approve their own agent's request. Default <see langword="false"/>: someone else must
    /// (four eyes). Only enforced for authenticated callers.
    /// </summary>
    public bool RequesterMayApprove { get; set; }

    /// <summary>How long the approval stays open. Default <see cref="KeelAgentsOptions.ApprovalLifetime"/>.</summary>
    public TimeSpan? Lifetime { get; set; }
}
