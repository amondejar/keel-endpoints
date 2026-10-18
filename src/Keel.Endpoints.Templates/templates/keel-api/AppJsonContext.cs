using System.Text.Json.Serialization;
using KeelApi.Features.Health;
using KeelApi.Features.Todos;
using KeelApi.Features.Todos.Create;

namespace KeelApi;

/// <summary>Native AOT: every type read from or written to the wire, for System.Text.Json source generation.</summary>
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(Todo))]
[JsonSerializable(typeof(CreateTodo))]
[JsonSerializable(typeof(HttpValidationProblemDetails))]
internal partial class AppJsonContext : JsonSerializerContext;
