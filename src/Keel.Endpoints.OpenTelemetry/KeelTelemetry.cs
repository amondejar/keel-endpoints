using System.Diagnostics;
using System.Reflection;

namespace Keel.Endpoints.Telemetry;

/// <summary>Names of the telemetry Keel.Endpoints emits, for exporters, dashboards and alerts.</summary>
public static class KeelTelemetry
{
    /// <summary>The <see cref="ActivitySource"/> of handler spans (<c>WithHandlerTelemetry()</c>).</summary>
    public const string ActivitySourceName = "Keel.Endpoints";

    /// <summary>The <see cref="System.Diagnostics.Metrics.Meter"/> of <c>keel.validation.errors</c> and <c>keel.handler.duration</c>.</summary>
    public const string MeterName = "Keel.Endpoints";

    /// <summary>Tag: the endpoint's operation name, as in OpenAPI (<c>InvoicesGet</c>).</summary>
    public const string OperationTag = "keel.operation";

    /// <summary>Tag: the endpoint's feature, from its namespace (<c>Invoices</c>).</summary>
    public const string FeatureTag = "keel.feature";

    /// <summary>Tag on <c>keel.validation.errors</c>: the member that failed validation (<c>Customer</c>, <c>Body.Customer</c>).</summary>
    public const string FieldTag = "keel.field";

    internal static readonly ActivitySource Source = new(
        ActivitySourceName,
        typeof(KeelTelemetry).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
}
