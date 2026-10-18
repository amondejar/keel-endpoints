using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Keel.Endpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddKeelEndpoints();
builder.Services.AddValidation();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<TodoStore>();
// Native AOT has no reflection-based JSON: every type on the wire is listed in the context below.
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));

var app = builder.Build();

// Generated at compile time: one explicit registration per endpoint class, no reflection.
app.MapKeelEndpoints("/api").AllowAnonymous();

app.Run();

public sealed record Todo(int Id, string Title, bool Done);

public sealed record GetTodo(int Id);

public sealed record CreateTodo([property: Required, StringLength(50)] string Title);

public sealed record WatchTodos(int Count = 3);

public sealed class TodoStore
{
    private readonly List<Todo> _todos = [new(1, "Try Keel.Endpoints with Native AOT", false)];

    public Todo? Find(int id) => _todos.Find(t => t.Id == id);

    public Todo Add(string title)
    {
        var todo = new Todo(_todos.Count + 1, title, false);
        _todos.Add(todo);
        return todo;
    }
}

namespace Todos.Get
{
    [Get("todos/{id}")]
    public sealed class Endpoint(TodoStore store) : ResultEndpoint<GetTodo, Results<Ok<Todo>, NotFound>>
    {
        public override Task<Results<Ok<Todo>, NotFound>> HandleAsync(GetTodo request, CancellationToken ct) =>
            Task.FromResult<Results<Ok<Todo>, NotFound>>(
                store.Find(request.Id) is { } todo ? TypedResults.Ok(todo) : TypedResults.NotFound());
    }
}

namespace Todos.Create
{
    [Post("todos")]
    public sealed class Endpoint(TodoStore store) : Endpoint<CreateTodo, Todo>
    {
        public override Task<Todo> HandleAsync(CreateTodo request, CancellationToken ct) => Task.FromResult(store.Add(request.Title));
    }
}

namespace Todos.Watch
{
    [Get("todos/events"), ServerSentEvents("todo")]
    public sealed class Endpoint : StreamEndpoint<WatchTodos, Todo>
    {
        public override async IAsyncEnumerable<Todo> HandleAsync(WatchTodos request, [EnumeratorCancellation] CancellationToken ct)
        {
            for (var i = 1; i <= request.Count; i++)
            {
                await Task.Yield();
                yield return new Todo(i, $"event {i}", false);
            }
        }
    }
}

namespace Health
{
    [Get("health"), AllowAnonymous]
    public sealed class Endpoint : EndpointWithoutRequest<string>
    {
        public override Task<string> HandleAsync(CancellationToken ct) => Task.FromResult("ok");
    }
}

[JsonSerializable(typeof(Todo))]
[JsonSerializable(typeof(CreateTodo))]
[JsonSerializable(typeof(HttpValidationProblemDetails))]
internal partial class AppJsonContext : JsonSerializerContext;
