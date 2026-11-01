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
2. The MCP client shows it to the user (Claude Code in a terminal, and other clients that support elicitation; some
   integrations, such as editor extensions, may answer *decline* without showing it).
3. The client calls again with the answer and the state. The server checks them, and only then runs the endpoint, with
   its binding, validation and authorization as usual.

The signed state is what makes it trustworthy:

- **Signed and encrypted** with ASP.NET Core Data Protection: a client cannot forge one.
- **Bound to the call**: the tool name, a hash of its arguments and the user. A confirmation for "refund 10 EUR" does
  not authorize "refund 10 000 EUR", nor the same call by another user.
- **Short-lived**: `ConfirmationLifetime`, 10 minutes by default.
- **Accepting confirms**: the *Confirm* box starts ticked, so *accept* runs the tool; unticking it, *decline* and
  *cancel* do not. A client that cannot show the form must answer *decline* or *cancel*, never *accept*.

Running several instances? Configure a shared Data Protection key ring, as for cookies.

## Fail-closed

| Situation | Result |
| --- | --- |
| The user declines, dismisses the form, or unticks *Confirm* | `403` tool error saying which, nothing runs |
| The state is expired, tampered with, or for other arguments or another user | `403` tool error, nothing runs |
| The client negotiated an older protocol, or does not support elicitation | `403` tool error naming the protocol it used, nothing runs (or an approval link, see below) |

There is no way for the agent to confirm on the user's behalf: the answer comes through the client's own UI.

## Approval by other people

Some actions should not be decided by the person whose agent asks: a large payout, closing an account. With
`RequireApproval` the tool does not run until people approve it through a link:

```csharp
builder.Services.AddKeelAgents();
app.MapKeelApprovals();                // the approval pages, at /approvals/{id}

options.ExposeAsTool("Pays money out to a supplier.", tool => tool.Destructive = false)
       .RequireApproval<Payout>(r => $"Pay {r.Amount} EUR to {r.To}");

options.ExposeAsTool("Closes a payment account.", tool => tool.Destructive = true)
       .RequireApproval("Close a payment account", approval =>
       {
           approval.Approvals = 2;             // two different people
           approval.Policy = "supervisors";    // who may approve
       });
```

1. The agent calls the tool. Nothing runs: the tool error says *Approval required* and gives a link.
2. The agent passes the link on. An approver opens it: the page shows the message, who asked, and Approve / Reject.
3. Once approved, the agent calls the tool again with the same arguments, and it runs, **once**. Another call needs
   a new approval. A rejection is reported to the agent once.

The rules, checked on every decision:

- **Four eyes** by default: the caller cannot approve their own agent's request (`RequesterMayApprove` to allow it).
- **Policy**: with `Policy`, approvers must sign in and satisfy it.
- **Several approvers** (`Approvals > 1`) must be different, authenticated people.
- **Bound to the call**: approving a payout of 500 EUR to ACME does not allow 50 000 EUR.
- **Expires**: `ApprovalLifetime`, 1 day by default.

The link is a 128-bit random secret. The page cannot be framed, is not cached, does not leak its URL in the referrer,
and only accepts the decision as JSON, which another site cannot send without a CORS preflight. `MapKeelApprovals`
returns the route group, to add rate limiting or `RequireAuthorization()`. The host does not start if a tool needs
approval and the pages are not mapped.

Approvals live in memory by default. On several instances, or to survive restarts, register an `IAgentApprovalStore`
(a database table): `TryReplaceAsync` is an optimistic concurrency check, so two retries cannot both run the tool.

### When the client cannot confirm

Some MCP clients cannot show a confirmation (older protocol revisions, some editor integrations). Instead of refusing,
fall back to a link that only the same user can approve:

```csharp
builder.Services.AddKeelAgents(o => o.WhenClientCannotConfirm = ConfirmationFallback.Approval);
app.MapKeelApprovals();
```

## Audit

Every agent call to a tool that changes data, and every step of a confirmation, is an `AgentAuditEntry`: time, tool,
user, decision (`NotRequired`, `ConfirmationRequested`, `Confirmed`, `Declined`, `Refused`, `ApprovalRequested`,
`ApprovalGranted` and `ApprovalRejected` with the approver as the user, `Approved`), the message the user saw,
a hash of the arguments (to correlate the steps without storing the data) and whether the endpoint succeeded. Reads
are not audited.

By default the entries go to the log (category `Keel.Endpoints.Agents`). Keep them in a store by registering your own
sink:

```csharp
builder.Services.AddSingleton<IAgentAuditSink, DatabaseAuditSink>();
```

## Not yet

- Automatic idempotency keys for tool calls, so an agent's retry does not repeat a write.
