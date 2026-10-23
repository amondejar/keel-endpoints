using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using Microsoft.AspNetCore.Http;

namespace Keel.Endpoints.Telemetry.Internal;

/// <summary>The instruments of the Keel.Endpoints meter, created through the app's <see cref="IMeterFactory"/>.</summary>
internal sealed class KeelInstruments
{
    // The same buckets as ASP.NET Core's http.server.request.duration, so the two line up on a dashboard.
    private static readonly IReadOnlyList<double> DurationBuckets = [0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10];

    private readonly Counter<long> _validationErrors;
    private readonly Histogram<double> _handlerDuration;

    public KeelInstruments(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(new MeterOptions(KeelTelemetry.MeterName)
        {
            Version = typeof(KeelInstruments).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        });

        _validationErrors = meter.CreateCounter<long>(
            "keel.validation.errors",
            unit: "{error}",
            description: "Request members that failed validation, one per member and request.");

        _handlerDuration = meter.CreateHistogram(
            "keel.handler.duration",
            unit: "s",
            description: "Time spent in endpoint handlers, after binding and validation.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = DurationBuckets });
    }

    public void RecordValidation(ProblemDetailsContext context)
    {
        if (!_validationErrors.Enabled) return;
        if (context.ProblemDetails is not HttpValidationProblemDetails { Errors.Count: > 0 } validation) return;
        if (EndpointTags.For(context.HttpContext.GetEndpoint()) is not { } tags) return;

        foreach (var field in validation.Errors.Keys)
        {
            _validationErrors.Add(1, new TagList
            {
                { KeelTelemetry.OperationTag, tags.Operation },
                { KeelTelemetry.FeatureTag, tags.Feature },
                { KeelTelemetry.FieldTag, field },
            });
        }
    }

    public void RecordHandler(double seconds, EndpointTags tags, string? errorType)
    {
        if (!_handlerDuration.Enabled) return;
        var list = new TagList
        {
            { KeelTelemetry.OperationTag, tags.Operation },
            { KeelTelemetry.FeatureTag, tags.Feature },
        };
        if (errorType is not null) list.Add("error.type", errorType);
        _handlerDuration.Record(seconds, list);
    }
}
