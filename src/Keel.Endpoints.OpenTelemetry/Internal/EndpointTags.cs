using System.Collections;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Keel.Endpoints.Telemetry.Internal;

/// <summary>The operation and feature of a Keel endpoint, computed once per endpoint.</summary>
internal sealed class EndpointTags
{
    private static readonly ConditionalWeakTable<Endpoint, EndpointTags?> Cache = new();
    private static readonly ConditionalWeakTable<Endpoint, EndpointTags?>.CreateValueCallback Create =
        endpoint => From(endpoint.Metadata);

    private EndpointTags(string operation, string feature)
    {
        Operation = operation;
        Feature = feature;
        SpanName = operation;
        LogScope = new Scope(operation, feature);
    }

    public string Operation { get; }

    public string Feature { get; }

    public string SpanName { get; }

    public IReadOnlyList<KeyValuePair<string, object?>> LogScope { get; }

    /// <summary>Null for endpoints that are not Keel endpoints.</summary>
    public static EndpointTags? For(Endpoint? endpoint) => endpoint is null ? null : Cache.GetValue(endpoint, Create);

    public static EndpointTags? From(IEnumerable<object> metadata)
    {
        KeelEndpointMetadata? keel = null;
        string? name = null;
        foreach (var item in metadata)
        {
            if (item is KeelEndpointMetadata k) keel = k;
            else if (item is IEndpointNameMetadata n) name = n.EndpointName;
        }

        if (keel is null) return null;
        return new EndpointTags(name ?? keel.EndpointType.Name, FeatureOf(keel.EndpointType));
    }

    /// <summary>
    /// Same rule as the OpenAPI tag: a class named <c>Endpoint</c> lives in its operation's namespace
    /// (<c>Invoices.Get</c>), so the feature is the segment before; any other class lives in its feature's.
    /// </summary>
    private static string FeatureOf(Type endpointType)
    {
        var segments = (endpointType.Namespace ?? string.Empty).Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return string.Empty;
        return endpointType.Name == "Endpoint" && segments.Length > 1 ? segments[^2] : segments[^1];
    }

    /// <summary>A logging scope that structured loggers read as properties and console loggers print readably.</summary>
    private sealed class Scope(string operation, string feature) : IReadOnlyList<KeyValuePair<string, object?>>
    {
        private readonly KeyValuePair<string, object?>[] _items =
            [new(KeelTelemetry.OperationTag, operation), new(KeelTelemetry.FeatureTag, feature)];

        public KeyValuePair<string, object?> this[int index] => _items[index];

        public int Count => _items.Length;

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => ((IEnumerable<KeyValuePair<string, object?>>)_items).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString() => $"{KeelTelemetry.OperationTag}:{operation} {KeelTelemetry.FeatureTag}:{feature}";
    }
}
