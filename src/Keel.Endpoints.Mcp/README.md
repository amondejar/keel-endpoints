# Keel.Endpoints.Mcp

Your Keel endpoints as [MCP](https://modelcontextprotocol.io) tools for AI agents, on the official MCP C# SDK.
One line per endpoint; the contract is the tool, and each call is the endpoint.

```csharp
builder.Services.AddKeelEndpoints();
builder.Services.AddKeelMcp();                 // MCP server, Streamable HTTP (stateless)

app.MapKeelEndpoints("/api");
app.MapKeelMcp("/mcp");

[Get("invoices/{id}"), Authorize]
public sealed class Endpoint(InvoiceStore store) : ResultEndpoint<GetInvoice, Results<Ok<InvoiceView>, NotFound>>
{
    public override void Configure(EndpointOptions options) => options.ExposeAsTool("Gets an invoice by its number.");
    // ...
}

[Delete("invoices/{id}"), Authorize("admin")]
public sealed class Endpoint(InvoiceStore store) : Endpoint<DeleteInvoice>
{
    public override void Configure(EndpointOptions options) =>
        options.ExposeAsTool("Deletes an invoice for good.", tool => tool.Destructive = true);
    // ...
}
```

| | |
| --- | --- |
| Opt-in | Only endpoints with `ExposeAsTool(description)` are tools |
| Name | The operation name in snake case: `Invoices.Get` is `invoices_get` (or `tool.Name`) |
| Input schema | The request contract through System.Text.Json, with its DataAnnotations (`Range`, `StringLength`, `Required`...) |
| Output schema | The response type (`T`, or the success value of `Results<Ok<T>, ...>`); the body also comes as structured content |
| A call | Runs the endpoint in process, as the caller: binding, validation, filters and ProblemDetails are the endpoint's |
| Authorization | The endpoint's own policies, with the MCP request's identity; tools a caller cannot use are not listed |
| Writes | Must set `Destructive` (`false` if it only adds, `true` if it may overwrite or delete), or the host does not start |
| Errors | A 4xx or 5xx is a tool error with the status and the endpoint's problem details; unhandled exceptions say nothing more |
| Reviews | The API manifest records each tool: `dotnet keel diff` reports renamed tools, new writes and lost confirmations |
| Discovery | Optional MCP Server Card at `/mcp/server-card` (`o.ServerCard = new() { Name, Description }`) |

See the [MCP guide](https://github.com/amondejar/keel-endpoints/blob/main/docs/mcp.md).
