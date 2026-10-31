namespace Keel.Endpoints.Agents;

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
}
