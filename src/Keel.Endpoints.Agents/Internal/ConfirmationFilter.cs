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
    ApprovalService approvals,
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
            var user = Identity.Of(context.User);

            if (endpoint.Metadata.GetMetadata<ApprovalMetadata>() is { } approval)
            {
                var o = approval.Options;
                return await ApproveAsync(context, next, name, user, hash, Message(approval.Message, tool, arguments),
                    new ApprovalSettings(o.Approvals, o.Policy, o.RequesterMayApprove, RequesterOnly: false, o.Lifetime ?? options.Value.ApprovalLifetime),
                    cancellationToken);
            }

            var confirmation = endpoint.Metadata.GetMetadata<ConfirmationMetadata>();
            var required = confirmation?.Required ?? (options.Value.ConfirmDestructiveTools && tool.Destructive == true);

            if (!required)
            {
                var result = await next(context, cancellationToken);
                if (!tool.ReadOnly) await Audit(name, user, AgentDecision.NotRequired, null, hash, Succeeded(result), cancellationToken);
                return result;
            }

            var summary = Message(confirmation?.Message, tool, arguments);

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
                if (options.Value.WhenClientCannotConfirm == ConfirmationFallback.Approval)
                {
                    // The same user confirms through a link instead.
                    return await ApproveAsync(context, next, name, user, hash, summary,
                        new ApprovalSettings(1, null, RequesterMayApprove: true, RequesterOnly: true, options.Value.ApprovalLifetime),
                        cancellationToken);
                }

                await Audit(name, user, AgentDecision.Refused, summary, hash, null, cancellationToken);
                var protocol = context.Server.NegotiatedProtocolVersion ?? "unknown";
                var why = context.Server.IsMrtrSupported
                    ? "it does not support elicitation"
                    : $"it connected with protocol {protocol}, and asking needs 2026-07-28";
                return Error($"This action needs the user's confirmation, and this MCP client cannot ask for it: {why}. Nothing was done.");
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

    /// <summary>
    /// A tool that needs approval: the first call opens an approval and answers with its link; later identical calls
    /// say it is still pending, report a rejection once, or run it once approved. Each approval is consumed by one call.
    /// </summary>
    private async ValueTask<CallToolResult> ApproveAsync(
        RequestContext<CallToolRequestParams> context,
        McpRequestHandler<CallToolRequestParams, CallToolResult> next,
        string name, string? user, string hash, string message, ApprovalSettings settings, CancellationToken cancellationToken)
    {
        var store = approvals.Store;
        var open = await store.FindOpenAsync(name, hash, user, cancellationToken);
        if (open is not null && open.Expires <= time.GetUtcNow()) open = null;

        if (open is null)
        {
            var created = await approvals.RequestAsync(name, hash, user, message, settings, cancellationToken);
            await Audit(name, user, AgentDecision.ApprovalRequested, message, hash, null, cancellationToken);
            return Error(Waiting(created, isNew: true));
        }

        switch (open.Status)
        {
            case AgentApprovalStatus.Pending:
                return Error(Waiting(open, isNew: false));

            case AgentApprovalStatus.Rejected:
                await store.TryReplaceAsync(open, open with { Consumed = true, Version = open.Version + 1 }, cancellationToken);
                return Error($"Rejected by {open.RejectedBy}. Nothing was done; do not try again unless the user asks.");

            default:
                // Consume first: two retries at once must not both run it.
                if (!await store.TryReplaceAsync(open, open with { Consumed = true, Version = open.Version + 1 }, cancellationToken))
                    return Error("This approval was just used by another call. Nothing was done twice.");
                var result = await next(context, cancellationToken);
                await Audit(name, user, AgentDecision.Approved, message, hash, Succeeded(result), cancellationToken);
                return result;
        }
    }

    private string Waiting(AgentApproval approval, bool isNew)
    {
        var who = approval.RequesterOnly
            ? "Ask the user to open this link and approve"
            : approval.RequiredApprovals > 1
                ? $"It needs {approval.RequiredApprovals} different approvers ({approval.ApprovedBy.Count} so far): share this link with them"
                : "Ask an approver to open this link";
        return $"Approval required{(isNew ? string.Empty : ", still pending")}: nothing was done yet. {who}: {approvals.Link(approval)} " +
               $"When it is approved, call this tool again with the same arguments. It expires at {approval.Expires.ToString("u", System.Globalization.CultureInfo.InvariantCulture)}.";
    }

    private string Message(Func<JsonElement, JsonSerializerOptions, string>? render, IAgentToolMetadata tool, IDictionary<string, JsonElement>? arguments)
    {
        var fallback = $"An AI agent wants to run '{tool.Name}': {tool.Description}";
        if (render is null) return fallback;
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
