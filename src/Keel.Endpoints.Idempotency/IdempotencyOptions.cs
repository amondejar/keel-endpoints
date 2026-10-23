namespace Keel.Endpoints.Idempotency;

/// <summary>Application-wide idempotency settings, set through <c>AddKeelIdempotency</c>.</summary>
public sealed class KeelIdempotencyOptions
{
    /// <summary>The request header carrying the key. <c>Idempotency-Key</c> by default, as in the IETF draft.</summary>
    public string HeaderName { get; set; } = "Idempotency-Key";

    /// <summary>How long a response is kept for retries. 24 hours by default.</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// How long a key stays claimed by a request that is still running. Retries in that window get 409. After it
    /// (a crashed instance, say) the key can be claimed again. 1 minute by default; keep it above your slowest handler.
    /// </summary>
    public TimeSpan InFlightTimeout { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Responses larger than this are sent but not stored, so a retry runs the handler again. 1 MiB by default.</summary>
    public int MaxResponseSize { get; set; } = 1024 * 1024;

    /// <summary>The longest key accepted; longer keys get 400. 255 by default.</summary>
    public int MaxKeyLength { get; set; } = 255;
}

/// <summary>Per-endpoint idempotency settings: <c>options.Idempotent(o =&gt; o.RequireKey = true)</c>.</summary>
public sealed class IdempotencyEndpointOptions
{
    /// <summary>Answer 400 when the header is missing, instead of running the request without protection.</summary>
    public bool RequireKey { get; set; }

    /// <summary>Overrides <see cref="KeelIdempotencyOptions.Retention"/> for this endpoint.</summary>
    public TimeSpan? Retention { get; set; }
}
