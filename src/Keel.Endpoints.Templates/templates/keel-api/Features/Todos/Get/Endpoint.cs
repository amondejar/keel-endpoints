using Keel.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;

namespace KeelApi.Features.Todos.Get;

/// <summary>The request contract, bound from the route (GET).</summary>
public sealed record GetTodo(int Id);

/// <summary>GET /api/todos/{id}</summary>
[Get("todos/{id}")]
public sealed class Endpoint(TodoStore store) : ResultEndpoint<GetTodo, Results<Ok<Todo>, NotFound>>
{
    public override Task<Results<Ok<Todo>, NotFound>> HandleAsync(GetTodo request, CancellationToken ct) =>
        Task.FromResult<Results<Ok<Todo>, NotFound>>(
            store.Find(request.Id) is { } todo ? TypedResults.Ok(todo) : TypedResults.NotFound());
}
