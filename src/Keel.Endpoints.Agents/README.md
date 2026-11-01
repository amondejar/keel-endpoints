# Keel.Endpoints.Agents

Safe actions for AI agents on your [Keel.Endpoints.Mcp](https://www.nuget.org/packages/Keel.Endpoints.Mcp) tools:
**the server**, not the agent, makes sure the user agreed before something irreversible happens, and records every
write an agent makes.

```csharp
builder.Services.AddKeelMcp();
builder.Services.AddKeelAgents();     // destructive tools now need the user's confirmation

[Post("solicitudes/{id}/rechazar"), Authorize("analista")]
public sealed class Endpoint(...) : Endpoint<RechazarSolicitud>
{
    public override void Configure(EndpointOptions options) => options
        .ExposeAsTool("Rejects a loan application.", tool => tool.Destructive = true)
        .RequireConfirmation<RechazarSolicitud>(r => $"Reject loan application {r.Id}: {r.Motivo}");
}
```

| | |
| --- | --- |
| Who asks | The server, through the MCP client (elicitation in a multi round-trip request, protocol `2026-07-28`, stateless) |
| What the user sees | Your message, built from the call's arguments, and a confirm box |
| Default | Every tool with `Destructive = true` asks; `RequireConfirmation` adds others, `WithoutConfirmation()` opts out |
| Fail-closed | No answer, a declined answer or a client that cannot ask: the endpoint does not run |
| Tamper-proof | The pending confirmation is signed (Data Protection), expires (10 minutes) and is bound to the tool, its arguments and the user |
| Approval | `RequireApproval(...)`: people approve through a link (`MapKeelApprovals()`), four eyes by default, a policy and several approvers if needed; the approved call runs once |
| Fallback | `WhenClientCannotConfirm = ConfirmationFallback.Approval`: a client that cannot show the confirmation gets a link the same user approves |
| Audit | Every agent call that changes data, every confirmation and every approval goes to `IAgentAuditSink` (the log by default) |

See the [agents guide](https://github.com/amondejar/keel-endpoints/blob/main/docs/agents.md).
