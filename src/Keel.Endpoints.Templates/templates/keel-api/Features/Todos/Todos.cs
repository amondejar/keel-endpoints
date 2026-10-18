using System.Collections.Concurrent;
using Keel.Endpoints;

namespace KeelApi.Features.Todos;

public sealed record Todo(int Id, string Title, bool Done);

/// <summary>An in-memory store standing in for a database.</summary>
public sealed class TodoStore
{
    private readonly ConcurrentDictionary<int, Todo> _todos = new();
    private int _next;

    public Todo Add(string title)
    {
        var todo = new Todo(Interlocked.Increment(ref _next), title, false);
        _todos[todo.Id] = todo;
        return todo;
    }

    public Todo? Find(int id) => _todos.GetValueOrDefault(id);
}

/// <summary>
/// Applies to every endpoint under Features.Todos. Replace AllowAnonymous with a real policy once
/// authentication is set up, for example options.RequireAuthorization("todos").
/// </summary>
public sealed class TodosGroup : EndpointGroup
{
    public override void Configure(EndpointOptions options) => options.AllowAnonymous();
}
