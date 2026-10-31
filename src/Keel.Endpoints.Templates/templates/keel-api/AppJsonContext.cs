using System.Text.Json.Serialization;
using KeelApi.Features.Health;
using KeelApi.Features.Todos;
using KeelApi.Features.Todos.Create;
#if (mcp)
using KeelApi.Features.Todos.Get;
#endif

namespace KeelApi;

/// <summary>Native AOT: every type read from or written to the wire, for System.Text.Json source generation.</summary>
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(Todo))]
[JsonSerializable(typeof(CreateTodo))]
#if (mcp)
[JsonSerializable(typeof(GetTodo))]   // MCP: every tool's input contract, for its schema
#endif
[JsonSerializable(typeof(HttpValidationProblemDetails))]
internal partial class AppJsonContext : JsonSerializerContext;
