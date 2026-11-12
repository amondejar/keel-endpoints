# MCP tools for AI agents

`Keel.Endpoints.Mcp` offers endpoints to AI agents (Claude, Copilot, any [MCP](https://modelcontextprotocol.io)
client) as tools, on the official MCP C# SDK. The request contract is the tool's input, the response its output, and a
tool call runs the endpoint itself. Nothing is exposed unless the endpoint says so.

## Setup

A new API can start with it: `dotnet new keel-api -n Shop --mcp` (also with `--aot`).

```csharp
builder.Services.AddKeelEndpoints();
builder.Services.AddValidation();
builder.Services.AddProblemDetails();
builder.Services.AddKeelMcp();          // returns the SDK's IMcpServerBuilder: add prompts, resources or more tools there

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapKeelEndpoints("/api");
app.MapKeelMcp("/mcp");                 // Streamable HTTP, stateless: scales like any other endpoint
```

Then, in each endpoint that agents should be able to use:

```csharp
public override void Configure(EndpointOptions options) =>
    options.ExposeAsTool("Creates an invoice for a customer.", tool => tool.Destructive = false);
```

The description is for the agent: say what the tool does and when to use it, in one or two sentences.

## What the agent sees

| Tool field | Comes from |
| --- | --- |
| `name` | The operation name in snake case (`Invoices.Get` → `invoices_get`, `Orders.GetV2` → `orders_get_v2`), or `tool.Name` |
| `description` | The `ExposeAsTool` description |
| `inputSchema` | The request contract, as System.Text.Json serializes it (the names the endpoint binds), with `Required`, `Range`, `StringLength`, `MinLength`, `MaxLength`, `Length`, `RegularExpression`, `EmailAddress`, `Url` and `[Description]` |
| `outputSchema` | The response type: `TResponse`, or `T` in `Results<Ok<T>, NotFound>` (also `Created<T>`, `Accepted<T>`) |
| `annotations` | `GET`: read-only. Other methods: `destructiveHint` from `tool.Destructive`, `idempotentHint` from `tool.Idempotent` (default: true for `PUT` and `DELETE`), `openWorldHint` from `tool.OpenWorld` (default false) |

A route parameter is a required argument. For `GET` and `DELETE` the other arguments are the query string (and
`[FromHeader]` members their headers); for `POST`, `PUT` and `PATCH` the arguments are the JSON body, exactly as the
endpoint's binding expects (`[BindRequest]` is respected).

`ExposeAsTool` belongs in each endpoint's own `Configure`: in an `EndpointGroup`, or twice on one endpoint, the host
does not start.

Clients on protocol revisions before `2026-07-28` require an output schema to be an object. For them a list or a
value is wrapped as `{"result": ...}`, in the schema and in the structured content; newer clients get it as is.

## Lean results

Every token of a tool result goes back to the model on each later turn of the conversation, so a smaller result is
cheaper for the whole session. The text the model reads is shaped; the structured content stays JSON that matches the
output schema (clients may validate it).

```csharp
builder.Services.AddKeelMcp(mcp =>
{
    mcp.Results.MaxItems = 50;          // cut long lists, and tell the agent how to narrow the call
    mcp.Results.FieldSelection = true;  // let the agent ask for some members only
});

// or per tool
options.ExposeAsTool("Lists loan applications.", tool =>
{
    tool.MaxItems = 20;
    tool.FieldSelection = true;
    tool.Format = McpResultFormat.Table;
});
```

| Option | Default | What the model gets |
| --- | --- | --- |
| `Compact` | on | No null members, no indentation |
| `MaxItems` | off | The first *n* items and *"Showing the first 20 of 312 items. To see the others, narrow the call with: status, minAmount."* (the tool's optional arguments) |
| `FieldSelection` | off | An optional `fields` argument listing the result's members; the agent gets only those. Unknown names are a `400` that lists the valid ones |
| `Format = Table` | `Json` | Lists of flat objects as a [TOON](https://github.com/toon-format/spec) table: member names once, then a line per item |

Measured on a list of 50 loan applications with 12 members, several of them optional (o200k tokenizer):

| Result | Tokens | |
| --- | --- | --- |
| The endpoint's JSON | 3,375 | |
| `Compact` (default) | 2,481 | −26 % |
| `MaxItems = 20` | 996 | −70 % |
| `fields` with 4 members | 1,061 | −69 % |
| `Format = Table` | 1,787 | −47 % |
| `fields` + `Table` | 702 | −79 % |

The tool definitions are leaner too: optional arguments (nullable members, or members with a default) are not listed as
required, so agents stop sending empty filters, and numbers are plain numbers in the schemas instead of
`["string","integer"]` plus a pattern (−39 % on this tool's definition, sent on every turn).

**On the table format**: a [2026 benchmark](https://arxiv.org/html/2605.29676v2) found TOON saves tokens but costs
accuracy with some models, more in long conversations. It is off by default: turn it on for the list tools where it
pays off, and check that your agent still answers right.

## Writes must say what they do

An endpoint that changes data (any method but `GET` and `HEAD`) must set `Destructive`, or the host does not start:

- `false`: it only adds (create an order, send a message).
- `true`: it may overwrite or delete (update a price, cancel an order, delete an invoice).

Agents and their users use the hint to decide what to confirm before calling. It is the same idea as Keel's
fail-closed authorization: a decision nobody made is an error, not a default.

## How a call runs

The call does not go back out over HTTP. Keel builds a request from the tool arguments and runs the endpoint's own
request delegate in process:

1. **Authorization first**, with the endpoint's policies (`[Authorize]`, `RequireAuthorization`, groups) and the MCP
   request's identity: whatever your authentication middleware put in `HttpContext.User` for the `/mcp` request. Not
   allowed: a `401` or `403` tool error. With `ListAuthorizedToolsOnly` (the default), a caller is not even shown the
   tools it cannot use.
2. **The endpoint**: binding, .NET 10 validation (or FluentValidation), endpoint filters, the handler and
   ProblemDetails, with the request's scoped services.
3. **The result**: a 2xx body is the tool's text content and, with an output schema, its structured content. A 4xx or
   5xx is a tool error with the status and the endpoint's answer, so a `400` validation problem tells the agent which
   field to fix; an error without a body (`404`, `409`) gets a sentence saying what it means and whether to retry. An unhandled exception is logged and reaches the agent as `500` with no details.

Middleware runs once, for the `/mcp` request, not per tool: rate limiting, CORS, output caching or request timeouts
configured on an endpoint do not apply to its tool calls. Put them on the MCP endpoint
(`app.MapKeelMcp().RequireRateLimiting("agents")`).

## Securing the MCP endpoint

Every call is authorized with the tool's own endpoint, so `/mcp` can stay open to anonymous callers that should only
see anonymous tools. To keep anonymous callers out entirely:

```csharp
app.MapKeelMcp("/mcp").RequireAuthorization();
```

## Reviewing tool changes

With an [API manifest](api-manifest.md) test, each tool is recorded next to its endpoint, and `dotnet keel diff` (or
the api-diff action on a pull request) reports what changes for agents:

```text
breaking   GET api/invoices/{id}: MCP tool renamed from 'invoices_get' to 'invoice_get': agents that use it stop working
security   DELETE api/invoices/{id}: MCP tool 'invoices_delete' is no longer marked destructive: agents may call it without asking the user
security   POST api/refunds: now the MCP tool 'refunds_create', which changes data: agents can call it
compatible GET api/invoices: MCP tool 'invoices_search' description changed to "Lists invoices of a customer."
```

`--fail-on security` stops a pull request that gives agents a new way to change data, or less reason to ask. The
description is shown in full because it is a prompt: review it like code.

## Discovery: the Server Card

An [MCP Server Card](https://modelcontextprotocol.io/seps/2127-mcp-server-cards) lets a client or a registry find the
server and how to connect before connecting. It is opt-in:

```csharp
builder.Services.AddKeelMcp(o => o.ServerCard = new()
{
    Name = "com.example/invoices",              // reverse DNS, a domain you control
    Description = "Invoices of Example Inc.",   // up to 100 characters
    Title = "Invoices",
    RepositoryUrl = "https://github.com/example/invoices",
    // Url = new Uri("https://api.example.com/mcp"),   // behind a proxy: the URL clients use
    // PublishAiCatalog = true,                        // also /.well-known/ai-catalog.json
});
```

`MapKeelMcp("/mcp")` then serves `GET /mcp/server-card` (`application/mcp-server-card+json`):

```json
{
  "$schema": "https://static.modelcontextprotocol.io/schemas/v1/server-card.schema.json",
  "name": "com.example/invoices",
  "version": "1.4.0",
  "description": "Invoices of Example Inc.",
  "title": "Invoices",
  "repository": { "url": "https://github.com/example/invoices", "source": "github" },
  "remotes": [ { "type": "streamable-http", "url": "https://api.example.com/mcp" } ]
}
```

- **Public**: anonymous, with `Access-Control-Allow-Origin: *`, even when the MCP endpoint requires authorization.
  Put nothing private in it.
- **Cacheable**: `Cache-Control: public, max-age=3600` and an `ETag`; `If-None-Match` gets a `304`.
- **No tools**: the spec leaves them out on purpose, since what a caller may call depends on who it is. Agents list
  them at runtime.
- **Consistent**: the version defaults to the application's, and the name and version become the server info the
  live connection reports, unless you set `McpServerOptions.ServerInfo` yourself.
- **Checked at startup**: a name that is not reverse-DNS, a missing or long description, or a version range stops the
  host.

Without `Url`, the card's URL comes from the request: set `AllowedHosts` in production so a forged `Host` header cannot
end up in a cached card. `PublishAiCatalog` adds `/.well-known/ai-catalog.json` with one entry for the card; leave it
off if something else on the domain publishes the catalog.

## Testing

Call the tools with the SDK's client, as an agent would:

```csharp
await using var client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
{
    Endpoint = new Uri(baseAddress + "/mcp"),
}));

var tools = await client.ListToolsAsync();
var result = await client.CallToolAsync("invoices_get", new Dictionary<string, object?> { ["id"] = 1 });
```

## Not yet

- Stream endpoints (`StreamEndpoint<,>`) cannot be tools: the host does not start if one is exposed.
- Form and file uploads are not tool arguments.
- An `Idempotency-Key` is not generated for tool calls by this package: with
  [Keel.Endpoints.Agents](agents.md#retries-that-do-not-repeat-a-write) it is, from the call itself.
