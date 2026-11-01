using Keel.Endpoints.Agents.Internal;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
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
        services.AddAuthorization();
        services.AddHttpContextAccessor();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IAgentAuditSink, LoggingAuditSink>();
        services.TryAddSingleton<IAgentApprovalStore, InMemoryApprovalStore>();
        services.TryAddSingleton<ApprovalRoutes>();
        services.TryAddSingleton<ApprovalService>();
        services.AddTransient<IStartupFilter, ApprovalRoutesVerifier>();
        services.TryAddSingleton<AgentToolIndex>();
        services.TryAddSingleton<ConfirmationTokens>();
        services.TryAddSingleton<ConfirmationFilter>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<McpServerOptions>, ConfirmationFilterSetup>());
        return services;
    }

    /// <summary>
    /// Maps the approval pages (default <c>/approvals/{id}</c>): what the agent asks, who asked, the status, and
    /// Approve / Reject. The links given to agents point here. Required by <c>RequireApproval</c> and by
    /// <see cref="ConfirmationFallback.Approval"/>. Returns the route group, for conventions such as rate limiting.
    /// </summary>
    public static RouteGroupBuilder MapKeelApprovals(this IEndpointRouteBuilder app, string pattern = "/approvals")
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        app.ServiceProvider.GetRequiredService<ApprovalRoutes>().Pattern = pattern;
        return ApprovalEndpoints.Map(app, pattern);
    }
}

/// <summary>Stops the host when approvals are needed but their pages are not mapped: agents would get dead links.</summary>
internal sealed class ApprovalRoutesVerifier(EndpointDataSource endpoints, ApprovalRoutes routes, IOptions<KeelAgentsOptions> options) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        next(app);
        if (routes.Pattern is not null) return;
        var needed = endpoints.Endpoints.FirstOrDefault(e => e.Metadata.GetMetadata<ApprovalMetadata>() is not null);
        if (needed is not null || options.Value.WhenClientCannotConfirm == ConfirmationFallback.Approval)
        {
            throw new InvalidOperationException(
                "Keel.Endpoints.Agents: " + (needed is not null ? $"{needed.DisplayName} requires approval" : "WhenClientCannotConfirm is Approval") +
                ", but the approval pages are not mapped. Call app.MapKeelApprovals().");
        }
    };
}
