using System.Diagnostics;
using Keel.Endpoints.Telemetry.Internal;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Keel.Endpoints.Telemetry;

/// <summary>Turns on Keel.Endpoints telemetry.</summary>
public static class KeelTelemetryExtensions
{
    /// <summary>
    /// Tags ASP.NET Core's request span and its <c>http.server.request.duration</c> measurement with
    /// <c>keel.operation</c> and <c>keel.feature</c> for every Keel endpoint, and counts validation errors per
    /// field in <c>keel.validation.errors</c> (needs <c>AddProblemDetails()</c>). No endpoint filter: the cost
    /// is two tags per request.
    /// </summary>
    public static IServiceCollection AddKeelTelemetry(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddMetrics();
        services.TryAddSingleton<KeelInstruments>();
        services.TryAddEnumerable(ServiceDescriptor.Transient<IStartupFilter, RequestTelemetryStartupFilter>());
        services.AddOptions<ProblemDetailsOptions>().PostConfigure<KeelInstruments>((options, instruments) =>
        {
            var previous = options.CustomizeProblemDetails;
            options.CustomizeProblemDetails = context =>
            {
                instruments.RecordValidation(context);
                previous?.Invoke(context);
            };
        });
        return services;
    }

    /// <summary>
    /// Adds, for the Keel endpoints under this builder (a route group such as <c>MapKeelEndpoints("/api")</c>,
    /// or one endpoint): a span around the handler, its duration in <c>keel.handler.duration</c>, and a logging
    /// scope with <c>keel.operation</c> and <c>keel.feature</c> for logs written by the handler. Binding and
    /// validation run before it, so the span and the duration are the handler's own. Costs an endpoint filter
    /// per request; requires <see cref="AddKeelTelemetry"/>.
    /// </summary>
    public static TBuilder WithHandlerTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddEndpointFilterFactory(static (factoryContext, next) =>
        {
            var instruments = factoryContext.ApplicationServices.GetService<KeelInstruments>()
                ?? throw new InvalidOperationException("WithHandlerTelemetry() needs builder.Services.AddKeelTelemetry().");
            var logger = factoryContext.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger("Keel.Endpoints");

            return async context =>
            {
                // Looked up per request (cached per endpoint): the factory context does not carry the metadata.
                if (EndpointTags.For(context.HttpContext.GetEndpoint()) is not { } tags) return await next(context);

                using var activity = KeelTelemetry.Source.StartActivity(tags.SpanName, ActivityKind.Internal);
                if (activity is { IsAllDataRequested: true })
                {
                    activity.SetTag(KeelTelemetry.OperationTag, tags.Operation);
                    activity.SetTag(KeelTelemetry.FeatureTag, tags.Feature);
                }

                using var scope = logger.BeginScope(tags.LogScope);
                var started = Stopwatch.GetTimestamp();
                string? error = null;
                try
                {
                    return await next(context);
                }
                catch (Exception e)
                {
                    error = e.GetType().FullName;
                    activity?.SetStatus(ActivityStatusCode.Error, e.Message);
                    activity?.SetTag("error.type", error);
                    throw;
                }
                finally
                {
                    instruments.RecordHandler(Stopwatch.GetElapsedTime(started).TotalSeconds, tags, error);
                }
            };
        });
        return builder;
    }
}
