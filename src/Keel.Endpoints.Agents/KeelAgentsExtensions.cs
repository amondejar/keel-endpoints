using Keel.Endpoints.Agents.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace Keel.Endpoints.Agents;

/// <summary>Registration of the agent safeguards.</summary>
public static class KeelAgentsExtensions
{
    /// <summary>
    /// Guards the Keel MCP tools (<c>AddKeelMcp</c>): tools with <c>RequireConfirmation</c>, and destructive ones by
    /// default, run only after the user confirms through the MCP client (elicitation, protocol <c>2026-07-28</c>); every
    /// call that changes data is written to the audit trail (<see cref="IAgentAuditSink"/>). Uses ASP.NET Core Data
    /// Protection to sign confirmations: configure a shared key ring when the app runs on several instances.
    /// </summary>
    public static IServiceCollection AddKeelAgents(this IServiceCollection services, Action<KeelAgentsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<KeelAgentsOptions>();
        if (configure is not null) services.Configure(configure);

        services.AddDataProtection();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IAgentAuditSink, LoggingAuditSink>();
        services.TryAddSingleton<AgentToolIndex>();
        services.TryAddSingleton<ConfirmationTokens>();
        services.TryAddSingleton<ConfirmationFilter>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<McpServerOptions>, ConfirmationFilterSetup>());
        return services;
    }
}
