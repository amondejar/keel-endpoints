using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Keel.Endpoints.Agents.Internal;

/// <summary>
/// A tools/call filter: for a tool that needs confirmation, the first call answers with an MRTR elicitation (and a
/// signed state) instead of running; the retry runs the tool only if the user accepted and the state matches this tool,
/// these arguments and this user. Everything that changes data is audited.
/// </summary>
internal sealed partial class ConfirmationFilter(
    AgentToolIndex index,
    ConfirmationTokens tokens,
    IAgentAuditSink audit,
    IOptions<KeelAgentsOptions> options,
    IOptions<JsonOptions> json,
    TimeProvider time,
    ILogger<ConfirmationFilter> logger)
{
    public const string InputKey = "keel_confirmation";
    public const string ConfirmField = "confirm";

    public McpRequestHandler<CallToolRequestParams, CallToolResult> Wrap(McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (context, cancellationToken) =>
        {
            var name = context.Params?.Name ?? string.Empty;
            if (!index.TryGet(name, out var endpoint, out var tool)) return await next(context, cancellationToken);

            var arguments = context.Params?.Arguments;
            var hash = ConfirmationTokens.Hash(arguments);
            var user = UserOf(context.User);
            var confirmation = endpoint.Metadata.GetMetadata<ConfirmationMetadata>();
            var required = confirmation?.Required ?? (options.Value.ConfirmDestructiveTools && tool.Destructive == true);

            if (!required)
            {
                var result = await next(context, cancellationToken);
                if (!tool.ReadOnly) await Audit(name, user, AgentDecision.NotRequired, null, hash, Succeeded(result), cancellationToken);
                return result;
            }

            var summary = Message(confirmation, tool, arguments);

            // The retry: the user's answer and the state we signed.
            if (context.Params?.RequestState is { } state && context.Params.InputResponses is { } responses && responses.TryGetValue(InputKey, out var response))
            {
                if (!tokens.IsValid(state, name, hash, user))
                {
                    await Audit(name, user, AgentDecision.Refused, summary, hash, null, cancellationToken);
                    return Error("The confirmation is not valid for this call: it expired, or the call or the user changed. Nothing was done; call the tool again to ask the user.");
                }

                var answer = response.Deserialize(InputResponse.ElicitResultJsonTypeInfo);
                var refusal = Refusal(answer);
                if (refusal is null)
                {
                    var result = await next(context, cancellationToken);
                    await Audit(name, user, AgentDecision.Confirmed, summary, hash, Succeeded(result), cancellationToken);
                    return result;
                }

                await Audit(name, user, AgentDecision.Declined, summary, hash, null, cancellationToken);
                return Error(refusal);
            }

            if (!context.Server.IsMrtrSupported || context.Server.ClientCapabilities is { Elicitation: null })
            {
                await Audit(name, user, AgentDecision.Refused, summary, hash, null, cancellationToken);
                return Error("This action needs the user's confirmation, and this MCP client cannot ask for it (elicitation over protocol 2026-07-28). Nothing was done.");
            }

            await Audit(name, user, AgentDecision.ConfirmationRequested, summary, hash, null, cancellationToken);
            throw new InputRequiredException(
                inputRequests: new Dictionary<string, InputRequest>
                {
                    [InputKey] = InputRequest.ForElicitation(new ElicitRequestParams
                    {
                        Message = summary,
                        RequestedSchema = new ElicitRequestParams.RequestSchema
                        {
                            Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                            {
                                [ConfirmField] = new ElicitRequestParams.BooleanSchema
                                {
                                    Title = "Confirm",
                                    Description = "Accept to do it. Untick to cancel.",
                                    Default = true,
                                },
                            },
                            Required = [ConfirmField],
                        },
                    }),
                },
                requestState: tokens.Create(name, hash, user));
        };

    /// <summary>
    /// Why the answer is not a confirmation, or <see langword="null"/> when it is. Accepting confirms: the box starts
    /// ticked, so only a user who unticks it on purpose cancels. Declining, dismissing, or a client that answers without
    /// asking (it must say <c>decline</c> or <c>cancel</c>) never confirms.
    /// </summary>
    internal static string? Refusal(ElicitResult? answer)
    {
        const string Dismissed = "The confirmation was dismissed without an answer (or the client could not show it). Nothing was done.";
        if (answer is null) return Dismissed;
        var unticked = answer.Content is { } content && content.TryGetValue(ConfirmField, out var confirm) && confirm.ValueKind == JsonValueKind.False;
        return answer.Action switch
        {
            "accept" when unticked => "The user accepted but unticked Confirm, so nothing was done. Ask the user before trying again.",
            "accept" => null,
            "decline" => "The user declined. Nothing was done; do not try again unless the user asks.",
            _ => Dismissed,
        };
    }

    private string Message(ConfirmationMetadata? confirmation, IAgentToolMetadata tool, IDictionary<string, JsonElement>? arguments)
    {
        var fallback = $"An AI agent wants to run '{tool.Name}': {tool.Description}";
        if (confirmation?.Message is not { } render) return fallback;
        try
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(arguments ?? new Dictionary<string, JsonElement>(), ArgumentsContext.Default.IDictionaryStringJsonElement));
            return render(document.RootElement, json.Value.SerializerOptions);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidCastException)
        {
            // Arguments the contract cannot read: the endpoint will answer 400 anyway; still ask with what we know.
            LogMessageFailed(logger, ex, tool.Name);
            return fallback;
        }
    }

    private ValueTask Audit(string tool, string? user, AgentDecision decision, string? summary, string hash, bool? succeeded, CancellationToken cancellationToken) =>
        audit.WriteAsync(new AgentAuditEntry(time.GetUtcNow(), tool, user, decision, summary, hash, succeeded), cancellationToken);

    private static bool Succeeded(CallToolResult result) => result.IsError is not true;

    private static string? UserOf(ClaimsPrincipal? user) =>
        user?.Identity?.IsAuthenticated == true
            ? user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.Identity.Name
            : null;

    private static CallToolResult Error(string message) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = $"{StatusCodes.Status403Forbidden} Forbidden: {message}" }],
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "The confirmation message of {Tool} could not be built from the arguments")]
    private static partial void LogMessageFailed(ILogger logger, Exception exception, string tool);
}

/// <summary>Adds the filter to the MCP server's tools/call pipeline.</summary>
internal sealed class ConfirmationFilterSetup(ConfirmationFilter filter) : IPostConfigureOptions<McpServerOptions>
{
    public void PostConfigure(string? name, McpServerOptions options) => options.Filters.Request.CallToolFilters.Add(filter.Wrap);
}

[System.Text.Json.Serialization.JsonSerializable(typeof(IDictionary<string, JsonElement>))]
internal sealed partial class ArgumentsContext : System.Text.Json.Serialization.JsonSerializerContext;
