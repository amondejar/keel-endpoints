using Keel.Endpoints.Telemetry;

namespace OpenTelemetry.Trace
{
    /// <summary>Keel.Endpoints on an OpenTelemetry tracer.</summary>
    public static class KeelTracerProviderBuilderExtensions
    {
        /// <summary>Collects Keel.Endpoints handler spans (<see cref="KeelTelemetry.ActivitySourceName"/>).
        /// Request spans come from ASP.NET Core instrumentation, tagged by <c>AddKeelTelemetry()</c>.</summary>
        public static TracerProviderBuilder AddKeelEndpointsInstrumentation(this TracerProviderBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            return builder.AddSource(KeelTelemetry.ActivitySourceName);
        }
    }
}

namespace OpenTelemetry.Metrics
{
    /// <summary>Keel.Endpoints on an OpenTelemetry meter.</summary>
    public static class KeelMeterProviderBuilderExtensions
    {
        /// <summary>Collects <c>keel.validation.errors</c> and <c>keel.handler.duration</c> (<see cref="KeelTelemetry.MeterName"/>).</summary>
        public static MeterProviderBuilder AddKeelEndpointsInstrumentation(this MeterProviderBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            return builder.AddMeter(KeelTelemetry.MeterName);
        }
    }
}
