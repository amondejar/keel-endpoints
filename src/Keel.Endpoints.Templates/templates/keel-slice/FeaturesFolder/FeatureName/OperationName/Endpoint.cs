#if (isPost || isUpdate)
using System.ComponentModel.DataAnnotations;
#endif
using Keel.Endpoints;
#if (isUpdate)
using Microsoft.AspNetCore.Mvc;
#endif
#if (isPost || isUpdate)
using Microsoft.Extensions.Validation;
#endif

namespace RootNs.FeaturesFolder.FeatureName.OperationName;

#if (isPost)
/// <summary>Read from the JSON body. Validated before the handler runs.</summary>
[ValidatableType]
public sealed record OperationNameRequest([property: Required, StringLength(100)] string Name);
#elif (isUpdate)
/// <summary>The id comes from the route, the changes from the JSON body.</summary>
[BindRequest(RequestBinding.Parameters), ValidatableType]
public sealed record OperationNameRequest(string Id, [property: FromBody] OperationNameBody Body);

[ValidatableType]
public sealed record OperationNameBody([property: Required, StringLength(100)] string Name);
#else
/// <summary>Bound from the route and the query string.</summary>
public sealed record OperationNameRequest(string Id);
#endif
#if (!isUpdate && !isDelete)

public sealed record OperationNameResponse(string Id);
#endif

// Authorization is fail-closed: decide who may call this endpoint before the app will start. Use
// [Authorize] or [AllowAnonymous] here, options.RequireAuthorization() in Configure, an EndpointGroup
// for the whole feature folder, or RequireAuthorization() on the route group in Program.cs.
#if (customRoute)
[VerbAttr("ROUTE_VALUE")]
#elif (isPost)
[VerbAttr("featurelower")]
#else
[VerbAttr("featurelower/{id}")]
#endif
#if (isUpdate || isDelete)
public sealed class Endpoint : Endpoint<OperationNameRequest>
{
    // Answers 204 No Content. Inject what the operation needs through the constructor.
    public override Task HandleAsync(OperationNameRequest request, CancellationToken ct)
    {
        throw new NotImplementedException();
    }
}
#else
public sealed class Endpoint : Endpoint<OperationNameRequest, OperationNameResponse>
{
    // Inject what the operation needs through the constructor.
    public override Task<OperationNameResponse> HandleAsync(OperationNameRequest request, CancellationToken ct)
    {
        throw new NotImplementedException();
    }
}
#endif
