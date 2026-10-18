using System.ComponentModel.DataAnnotations;
using Keel.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Validation;

namespace KeelApi.Features.Todos.Create;

/// <summary>The request contract, read from the JSON body (POST). Validated before the handler runs.</summary>
[ValidatableType]
public sealed record CreateTodo([property: Required, StringLength(100)] string Title);

/// <summary>POST /api/todos</summary>
[Post("todos")]
public sealed class Endpoint(TodoStore store) : ResultEndpoint<CreateTodo, Created<Todo>>
{
    public override Task<Created<Todo>> HandleAsync(CreateTodo request, CancellationToken ct)
    {
        var todo = store.Add(request.Title);
        return Task.FromResult(TypedResults.Created($"/api/todos/{todo.Id}", todo));
    }
}
