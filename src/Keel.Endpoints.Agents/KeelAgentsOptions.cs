namespace Keel.Endpoints.Agents;

/// <summary>What happens when a confirmation is needed and the MCP client cannot show it.</summary>
public enum ConfirmationFallback
{
    /// <summary>Refuse: nothing runs. The default.</summary>
    Refuse,

    /// <summary>Ask the user to approve through a link instead (<c>MapKeelApprovals</c>); the agent's next identical call runs it.</summary>
    Approval,
}

/// <summary>Options of the agent safeguards (<c>AddKeelAgents</c>).</summary>
public sealed class KeelAgentsOptions
{
    /// <summary>
    /// Asks the user to confirm every tool marked destructive (<c>tool.Destructive = true</c>), even without
    /// <c>RequireConfirmation</c>. Default <see langword="true"/>; an endpoint opts out with <c>WithoutConfirmation()</c>.
    /// </summary>
    public bool ConfirmDestructiveTools { get; set; } = true;

    /// <summary>How long the user has to answer a confirmation. Default 10 minutes.</summary>
    public TimeSpan ConfirmationLifetime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// When a confirmation is needed and the client cannot show it (an older protocol, or no elicitation): refuse
    /// (default), or fall back to an approval link that only the same user can approve.
    /// </summary>
    public ConfirmationFallback WhenClientCannotConfirm { get; set; } = ConfirmationFallback.Refuse;

    /// <summary>How long an approval stays open. Default 1 day.</summary>
    public TimeSpan ApprovalLifetime { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// The public base URL of the approval pages (<c>https://app.example.com/approvals</c>), for the links given to agents.
    /// Default: built from the MCP request and the <c>MapKeelApprovals</c> pattern.
    /// </summary>
    public Uri? ApprovalsUrl { get; set; }
}
