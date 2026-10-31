using Keel.Endpoints.Mcp.Internal;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Hosting;

namespace Keel.Endpoints.Mcp;

/// <summary>Registration and mapping of the MCP server built from the Keel endpoints.</summary>
public static class KeelMcpExtensions
{
    /// <summary>
    /// Registers an MCP server (Streamable HTTP, stateless) whose tools are the Keel endpoints that call
    /// <c>ExposeAsTool</c>. Returns the MCP server builder, so the official SDK's options stay available (more tools,
    /// prompts, resources, filters). Map it with <see cref="MapKeelMcp"/>.
    /// </summary>
    public static IMcpServerBuilder AddKeelMcp(this IServiceCollection services, Action<KeelMcpOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<KeelMcpOptions>();
        if (configure is not null) services.Configure(configure);

        services.AddHttpContextAccessor();
        services.AddAuthorization();
        services.TryAddSingleton<ToolTable>();
        services.TryAddSingleton<ToolInvoker>();
        // Builds the tool table once the request pipeline (and so the endpoint table) is built, so a misconfigured
        // tool stops the host at startup instead of failing its first call.
        services.AddTransient<IStartupFilter, ToolTableVerifier>();

        return services.AddMcpServer()
            .WithHttpTransport()
            .WithListToolsHandler((request, ct) => Invoker(request.Services).ListAsync(request, ct))
            .WithCallToolHandler((request, ct) => Invoker(request.Services).CallAsync(request, ct));
    }

    /// <summary>
    /// Maps the MCP endpoint (default <c>/mcp</c>). Each tool call is authorized with its own endpoint's policies, so the
    /// MCP endpoint itself can stay open; require authorization on it too if no anonymous caller should reach it.
    /// </summary>
    public static IEndpointConventionBuilder MapKeelMcp(this IEndpointRouteBuilder app, string pattern = "/mcp")
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.MapMcp(pattern);
    }

    private static ToolInvoker Invoker(IServiceProvider? services) =>
        (services ?? throw new InvalidOperationException("The MCP request has no services.")).GetRequiredService<ToolInvoker>();
}

internal sealed class ToolTableVerifier(ToolTable table) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        next(app);
        _ = table.Tools;
    };
}
