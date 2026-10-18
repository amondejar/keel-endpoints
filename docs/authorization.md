# Authorization

Every endpoint must say who may call it. If one does not, the application refuses to start, naming the
endpoint; an endpoint can never be public by accident.

## Where the decision can be made

| Where | Example |
| --- | --- |
| The route group | `app.MapKeelEndpoints("/api").RequireAuthorization();` |
| An endpoint group | `options.RequireAuthorization("billing")` in an `EndpointGroup` |
| The class | `[Authorize(Policy = "admin")]`, `[AllowAnonymous]` |
| `Configure` | `options.RequireAuthorization("admin")`, `options.AllowAnonymous()` |

They combine as in ASP.NET Core: policies from every level must all pass, and `AllowAnonymous` anywhere
wins. A typical API requires authentication on the route group and marks the few public endpoints
`[AllowAnonymous]`.

## Turning the check off

```csharp
builder.Services.AddKeelEndpoints(o => o.RequireExplicitAuthorization = false);
```

The [API manifest](api-manifest.md) records each endpoint's authorization, so a pull request that makes an
endpoint public shows up as a security change.
