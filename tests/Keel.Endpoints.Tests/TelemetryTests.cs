using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using Keel.Endpoints.Telemetry;
using Keel.Endpoints.Tests.TelemetryFixtures.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Validation;
using static Keel.Endpoints.Tests.Harness;

namespace Keel.Endpoints.Tests;

internal static class TelemetryTests
{
    public static void Register()
    {
        Test("Telemetry: request span and http.server.request.duration carry keel.operation and keel.feature", async () =>
        {
            using var capture = new Capture();
            await using var app = await StartAsync(handlerTelemetry: false);
            Assert.Equal(HttpStatusCode.OK, (await Client(app).GetAsync("/api/orders/7")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Client(app).GetAsync("/plain")).StatusCode);

            var request = capture.Activities.Single(a => a.Source.Name == "Microsoft.AspNetCore" && a.GetTagItem("keel.operation") is not null);
            Assert.Equal("OrdersGet", request.GetTagItem("keel.operation"));
            Assert.Equal("Orders", request.GetTagItem("keel.feature"));

            var durations = capture.Measurements("http.server.request.duration");
            Assert.True(durations.Any(m => m.Tag("keel.operation") == "OrdersGet" && m.Tag("keel.feature") == "Orders" && m.Tag("http.route") == "/api/orders/{id}"));
            // Endpoints that are not Keel endpoints are left alone.
            Assert.True(durations.Any(m => m.Tag("http.route") == "/plain" && m.Tag("keel.operation") is null));
            // No handler spans unless WithHandlerTelemetry() is on.
            Assert.True(!capture.Activities.Any(a => a.Source.Name == KeelTelemetry.ActivitySourceName));
        });

        Test("Telemetry: keel.validation.errors counts each failed member, and the request is still tagged", async () =>
        {
            using var capture = new Capture();
            await using var app = await StartAsync(handlerTelemetry: true);
            var response = await Client(app).PostAsJsonAsync("/api/orders", new { name = "", quantity = 50 });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var errors = capture.Measurements("keel.validation.errors");
            Assert.Equal("Name,Quantity", string.Join(",", errors.Select(e => e.Tag("keel.field")).Order(StringComparer.OrdinalIgnoreCase)));
            Assert.True(errors.All(e => e.Value == 1 && e.Tag("keel.operation") == "OrdersCreate" && e.Tag("keel.feature") == "Orders"));
            Assert.True(capture.Measurements("http.server.request.duration").Any(m => m.Tag("keel.operation") == "OrdersCreate" && m.Tag("http.response.status_code") == "400"));
            // Validation answered before the handler: nothing timed.
            Assert.Equal(0, capture.Measurements("keel.handler.duration").Count);
        });

        Test("Telemetry: WithHandlerTelemetry() adds a handler span, keel.handler.duration and a logging scope", async () =>
        {
            using var capture = new Capture();
            var logs = new ScopeCapturingLoggerProvider();
            await using var app = await StartAsync(handlerTelemetry: true, logs);
            Assert.Equal(HttpStatusCode.OK, (await Client(app).GetAsync("/api/orders/7")).StatusCode);

            var handler = capture.Activities.Single(a => a.Source.Name == KeelTelemetry.ActivitySourceName);
            var request = capture.Activities.Single(a => a.Source.Name == "Microsoft.AspNetCore" && a.GetTagItem("keel.operation") is not null);
            Assert.Equal("OrdersGet", handler.DisplayName);
            Assert.Equal(request.SpanId, handler.ParentSpanId);
            Assert.Equal("Orders", handler.GetTagItem("keel.feature"));

            var duration = capture.Measurements("keel.handler.duration").Single();
            Assert.True(duration.Tag("keel.operation") == "OrdersGet" && duration.Value >= 0 && duration.Tag("error.type") is null);

            var log = logs.Entries.Single(e => e.Message == "Reading order 7");
            Assert.True(log.Scope.Contains("keel.operation=OrdersGet") && log.Scope.Contains("keel.feature=Orders"));
        });

        Test("Telemetry: a handler that throws records error.type and marks its span as an error", async () =>
        {
            using var capture = new Capture();
            await using var app = await StartAsync(handlerTelemetry: true);
            Assert.Equal(HttpStatusCode.InternalServerError, (await Client(app).GetAsync("/api/orders/boom")).StatusCode);

            var handler = capture.Activities.Single(a => a.Source.Name == KeelTelemetry.ActivitySourceName);
            Assert.Equal(ActivityStatusCode.Error, handler.Status);
            Assert.Equal("System.InvalidOperationException", capture.Measurements("keel.handler.duration").Single().Tag("error.type"));
        });

        Test("Telemetry: WithHandlerTelemetry() without AddKeelTelemetry() names the missing call", async () =>
        {
            var builder = WebApplication.CreateBuilder(["--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default=None"]);
            builder.Services.AddKeelEndpoints();
            await using var app = builder.Build();
            app.MapGroup("/api").AllowAnonymous().WithHandlerTelemetry().MapEndpoint<TelemetryFixtures.Orders.Get.Endpoint>();
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => app.StartAsync());
            Assert.True(error.Message.Contains("AddKeelTelemetry()"));
        });
    }

    private static async Task<WebApplication> StartAsync(bool handlerTelemetry, ILoggerProvider? logs = null)
    {
        var builder = WebApplication.CreateBuilder(["--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default=None"]);
        if (logs is not null)
        {
            builder.Logging.ClearProviders().AddProvider(logs).SetMinimumLevel(LogLevel.Information);
            builder.Logging.AddFilter("Keel.Endpoints.Tests", LogLevel.Information);
        }

        builder.Services.AddKeelEndpoints();
        builder.Services.AddTestValidation();
        builder.Services.AddProblemDetails();
        builder.Services.AddKeelTelemetry();
        builder.Services.AddSingleton<OrderBook>();
        var app = builder.Build();

        var api = app.MapGroup("/api");
        if (handlerTelemetry) api.WithHandlerTelemetry();
        api.MapEndpoint<TelemetryFixtures.Orders.Get.Endpoint>();
        api.MapEndpoint<TelemetryFixtures.Orders.Create.Endpoint>();
        app.MapGet("/plain", () => "plain");

        await app.StartAsync();
        return app;
    }

    private static HttpClient Client(WebApplication app) => new() { BaseAddress = new Uri(app.Urls.First()) };

    internal sealed record Measurement(string Instrument, double Value, KeyValuePair<string, object?>[] Tags)
    {
        public string? Tag(string name) => Tags.FirstOrDefault(t => t.Key == name).Value?.ToString();
    }

    /// <summary>Listens to ASP.NET Core's and Keel's activity sources and meters, as an exporter would.</summary>
    private sealed class Capture : IDisposable
    {
        private readonly ActivityListener _activities;
        private readonly MeterListener _meters = new();
        private readonly ConcurrentQueue<Measurement> _measurements = new();

        public ConcurrentQueue<Activity> Activities { get; } = new();

        public Capture()
        {
            _activities = new ActivityListener
            {
                ShouldListenTo = s => s.Name is "Microsoft.AspNetCore" or KeelTelemetry.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = Activities.Enqueue,
            };
            ActivitySource.AddActivityListener(_activities);

            _meters.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name is "Microsoft.AspNetCore.Hosting" or KeelTelemetry.MeterName) listener.EnableMeasurementEvents(instrument);
            };
            _meters.SetMeasurementEventCallback<double>((i, v, tags, _) => _measurements.Enqueue(new(i.Name, v, tags.ToArray())));
            _meters.SetMeasurementEventCallback<long>((i, v, tags, _) => _measurements.Enqueue(new(i.Name, v, tags.ToArray())));
            _meters.Start();
        }

        /// <summary>Measurements are recorded when the response completes, just after the client sees it.</summary>
        public List<Measurement> Measurements(string instrument)
        {
            Thread.Sleep(100);
            return [.. _measurements.Where(m => m.Instrument == instrument)];
        }

        public void Dispose()
        {
            _activities.Dispose();
            _meters.Dispose();
        }
    }

    private sealed class ScopeCapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

        public ConcurrentQueue<(string Message, string Scope)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

        public void Dispose() { }

        private sealed class Logger(ScopeCapturingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => owner._scopes.Push(state);

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var scope = new List<string>();
                owner._scopes.ForEachScope((s, list) =>
                {
                    if (s is IEnumerable<KeyValuePair<string, object?>> pairs) list.AddRange(pairs.Select(p => $"{p.Key}={p.Value}"));
                }, scope);
                owner.Entries.Enqueue((formatter(state, exception), string.Join(";", scope)));
            }
        }
    }
}
