using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Keel.Endpoints.Telemetry.Internal;

/// <summary>Puts the request-tagging middleware in front of the app's own pipeline.</summary>
internal sealed class RequestTelemetryStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(static next => context => RequestTelemetry.InvokeAsync(context, next));
        next(app);
    };
}

internal static class RequestTelemetry
{
    /// <summary>
    /// Tags after the request ran: routing (later in the pipeline) has chosen the endpoint by then, and both
    /// the request span and the duration measurement are still open, whatever answered (the handler,
    /// validation, authorization).
    /// </summary>
    public static async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        try
        {
            await next(context);
        }
        finally
        {
            Tag(context);
        }
    }

    private static void Tag(HttpContext context)
    {
        if (EndpointTags.For(context.GetEndpoint()) is not { } tags) return;

        if (context.Features.Get<IHttpActivityFeature>()?.Activity is { IsAllDataRequested: true } activity)
        {
            activity.SetTag(KeelTelemetry.OperationTag, tags.Operation);
            activity.SetTag(KeelTelemetry.FeatureTag, tags.Feature);
        }

        if (context.Features.Get<IHttpMetricsTagsFeature>() is { } metrics)
        {
            metrics.Tags.Add(new(KeelTelemetry.OperationTag, tags.Operation));
            metrics.Tags.Add(new(KeelTelemetry.FeatureTag, tags.Feature));
        }
    }
}
