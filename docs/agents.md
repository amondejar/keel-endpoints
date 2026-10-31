# Safe actions for AI agents

`Keel.Endpoints.Agents` sits on top of [Keel.Endpoints.Mcp](mcp.md). An agent can call any tool it is allowed to, but
some actions should not happen just because a model decided so: cancelling an order, refunding a payment, rejecting a
loan application. For those, **the server asks the user** and runs the endpoint only after a yes. It also writes an
audit entry for every agent call that changes data.

## Setup

```csharp
builder.Services.AddKeelMcp();
builder.Services.AddKeelAgents();
```

From then on every tool with `Destructive = true` asks for confirmation. Opt in other endpoints, or opt out, in their
`Configure`:

```csharp
// Not destructive, but money leaves: ask, and show what will happen.
options.ExposeAsTool("Refunds a payment.", tool => tool.Destructive = false)
       .RequireConfirmation<RefundPayment>(r => $"Refund payment {r.Id}: {r.Amount} EUR");

// Destructive, but easy to undo: don't ask (still audited).
options.ExposeAsTool("Archives a payment.", tool => tool.Destructive = true)
       .WithoutConfirmation();
```

`RequireConfirmation<TRequest>` reads the call's arguments as the endpoint's request contract, so the message shows the
real data: the user approves *this* refund, not "a refund".

## How it works

The confirmation uses MCP's multi round-trip requests (protocol `2026-07-28`), so it works on the stateless Streamable
HTTP server Keel uses:

1. The agent calls the tool. The server does not run it: it answers with an **elicitation** (your message and a
   *Confirm* box) and a signed state.
2. The MCP client shows it to the user (Claude, VS Code and other clients that support elicitation).
3. The client calls again with the answer and the state. The server checks them, and only then runs the endpoint, with
   its binding, validation and authorization as usual.

The signed state is what makes it trustworthy:

- **Signed and encrypted** with ASP.NET Core Data Protection: a client cannot forge one.
- **Bound to the call**: the tool name, a hash of its arguments and the user. A confirmation for "refund 10 EUR" does
  not authorize "refund 10 000 EUR", nor the same call by another user.
- **Short-lived**: `ConfirmationLifetime`, 10 minutes by default.
- **Strict about the answer**: only *accept* with the box ticked (`confirm: true`) runs the tool.

Running several instances? Configure a shared Data Protection key ring, as for cookies.

## Fail-closed

| Situation | Result |
| --- | --- |
| The user declines, cancels, or accepts without ticking the box | `403` tool error, nothing runs |
| The state is expired, tampered with, or for other arguments or another user | `403` tool error, nothing runs |
| The client negotiated an older protocol, or does not support elicitation | `403` tool error saying the client cannot ask, nothing runs |

There is no way for the agent to confirm on the user's behalf: the answer comes through the client's own UI.

## Audit

Every agent call to a tool that changes data, and every step of a confirmation, is an `AgentAuditEntry`: time, tool,
user, decision (`NotRequired`, `ConfirmationRequested`, `Confirmed`, `Declined`, `Refused`), the message the user saw,
a hash of the arguments (to correlate the steps without storing the data) and whether the endpoint succeeded. Reads
are not audited.

By default the entries go to the log (category `Keel.Endpoints.Agents`). Keep them in a store by registering your own
sink:

```csharp
builder.Services.AddSingleton<IAgentAuditSink, DatabaseAuditSink>();
```

## Not yet

- Approval by someone other than the caller (a manager, two people), out of band.
- Automatic idempotency keys for tool calls, so an agent's retry does not repeat a write.
