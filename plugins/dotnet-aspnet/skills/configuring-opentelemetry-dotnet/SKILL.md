---
name: configuring-opentelemetry-dotnet
description: Configure OpenTelemetry distributed tracing, metrics, and logging in ASP.NET Core using the .NET OpenTelemetry SDK. Use when adding observability, setting up OTLP exporters, creating custom metrics/spans, or troubleshooting distributed trace correlation.
license: MIT
---

# Configuring OpenTelemetry in .NET

## When to Use

- Adding distributed tracing to an ASP.NET Core application
- Setting up OpenTelemetry exporters (OTLP is the primary protocol; Jaeger accepts OTLP natively; Prometheus OTLP ingestion requires explicit opt-in)
- Creating custom metrics or trace spans for business operations
- Troubleshooting distributed trace context propagation across services

## When Not to Use

- The user wants application-level logging only (use ILogger, Serilog)
- The user is using Application Insights SDK directly (different API)
- The user needs APM with a commercial vendor's proprietary SDK

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| ASP.NET Core project | Yes | The application to instrument |
| Observability backend | No | Where to export: OTLP collector, Aspire dashboard, Jaeger (accepts OTLP natively) |

## Workflow

### Step 1: Install the correct packages

**There are many OpenTelemetry NuGet packages. Install exactly these:**

```bash
# Core SDK + ASP.NET Core instrumentation + logging integration
dotnet add package OpenTelemetry.Extensions.Hosting
dotnet add package OpenTelemetry.Instrumentation.AspNetCore
dotnet add package OpenTelemetry.Instrumentation.Http

# Exporter
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol  # OTLP exporter for traces, metrics, AND logs

# Optional — dev/local debugging only (do NOT include in production deployments)
# dotnet add package OpenTelemetry.Exporter.Console
```

**Do NOT install `OpenTelemetry` alone** — you need `OpenTelemetry.Extensions.Hosting` for proper DI integration.

#### Optional: additional auto-instrumentation packages

Install only the packages that match the libraries your application uses:

```bash
dotnet add package OpenTelemetry.Instrumentation.SqlClient           # SQL Server queries
dotnet add package OpenTelemetry.Instrumentation.EntityFrameworkCore  # EF Core
dotnet add package OpenTelemetry.Instrumentation.GrpcNetClient       # gRPC calls
dotnet add package OpenTelemetry.Instrumentation.Runtime             # GC, thread pool metrics
```

### Step 2: Configure all signals in Program.cs

```csharp
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using OpenTelemetry.Logs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(serviceName: builder.Environment.ApplicationName))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation(options =>
        {
            // Filter out health check endpoints from traces
            options.Filter = httpContext =>
                !httpContext.Request.Path.StartsWithSegments("/healthz");
        })
        .AddHttpClientInstrumentation(options =>
        {
            options.RecordException = true;
        })
        // Optional: add SQL instrumentation if using SqlClient directly
        // .AddSqlClientInstrumentation(options =>
        // {
        //     options.SetDbStatementForText = true;
        //     options.RecordException = true;
        // })
        // Custom activity sources (must match ActivitySource names in your code)
        .AddSource("MyApp.Orders")
        .AddSource("MyApp.Payments")
        .AddSource("MyApp.Messaging"))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        // Optional: .AddRuntimeInstrumentation() for GC and thread pool metrics
        //   (requires OpenTelemetry.Instrumentation.Runtime package)
        // Custom meters (must match Meter names in your code)
        .AddMeter("MyApp.Metrics"))
    .WithLogging(logging =>
    {
        logging.IncludeScopes = true;
        // logging.IncludeFormattedMessage = true;  // Enable if you need the formatted message string in log exports
    })
    // Single OTLP exporter for all signals — reads OTEL_EXPORTER_OTLP_ENDPOINT
    // env var (defaults to http://localhost:4317). Override via environment variable
    // or appsettings.json configuration.
    .UseOtlpExporter();
```

### Step 3: Understanding log–trace correlation

The `.WithLogging()` call in Step 2 integrates ILogger with OpenTelemetry:

- Each log entry automatically includes TraceId and SpanId for correlation with traces
- The service resource from `.ConfigureResource()` propagates to logs automatically
- `UseOtlpExporter()` applies to logs alongside traces and metrics
- No additional packages or separate `SetResourceBuilder` call needed

### Step 4: Create custom spans (Activities) for business operations

```csharp
using System.Diagnostics;
using Microsoft.Extensions.Logging;

public class OrderService
{
    // Create an ActivitySource matching what you registered in Step 2
    private static readonly ActivitySource ActivitySource = new("MyApp.Orders");
    private readonly ILogger<OrderService> _logger;

    public OrderService(ILogger<OrderService> logger) => _logger = logger;

    public async Task<Order> ProcessOrderAsync(CreateOrderRequest request)
    {
        // Start a new span
        using var activity = ActivitySource.StartActivity("ProcessOrder");

        // Add attributes (tags) to the span
        activity?.SetTag("order.customer_id", request.CustomerId);
        activity?.SetTag("order.item_count", request.Items.Count);

        try
        {
            // Child span for validation
            using (var validationActivity = ActivitySource.StartActivity("ValidateOrder"))
            {
                await ValidateOrderAsync(request);
                validationActivity?.SetTag("validation.result", "passed");
            }

            // Child span for payment
            using (var paymentActivity = ActivitySource.StartActivity("ProcessPayment",
                ActivityKind.Client))  // Client = outgoing call
            {
                paymentActivity?.SetTag("payment.method", request.PaymentMethod);
                await ProcessPaymentAsync(request);
            }

            var order = new Order { Id = Guid.NewGuid(), CustomerId = request.CustomerId, Status = "Completed" };

            activity?.SetTag("order.status", "completed");
            activity?.SetStatus(ActivityStatusCode.Ok);

            return order;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            // Log via ILogger — OpenTelemetry captures this with trace correlation.
            // Prefer logging over activity.RecordException() as OTel is deprecating
            // span events for exception recording in favor of log-based exceptions.
            _logger.LogError(ex, "Order processing failed for customer {CustomerId}", request.CustomerId);
            throw;
        }
    }
}
```

**Critical: `ActivitySource` name must match `AddSource("...")` in configuration.** Unmatched sources are silently ignored — this is the #1 debugging issue.

### Step 5: Create custom metrics

Use `IMeterFactory` (injected via DI) to create meters — this ensures proper lifetime management and testability.

```csharp
using System.Diagnostics;
using System.Diagnostics.Metrics;

public class OrderMetrics
{
    private readonly Counter<long> _ordersProcessed;
    private readonly Histogram<double> _orderProcessingDuration;
    private readonly UpDownCounter<int> _activeOrders;

    public OrderMetrics(IMeterFactory meterFactory)
    {
        // Meter name must match AddMeter("...") in configuration
        var meter = meterFactory.Create("MyApp.Metrics");

        // Counter — use for things that only go up
        _ordersProcessed = meter.CreateCounter<long>(
            "orders.processed", "orders", "Total orders successfully processed");

        // Histogram — use for measuring distributions (latency, sizes)
        _orderProcessingDuration = meter.CreateHistogram<double>(
            "orders.processing_duration", "ms", "Time to process an order");

        // UpDownCounter — use for things that go up AND down
        _activeOrders = meter.CreateUpDownCounter<int>(
            "orders.active", "orders", "Currently processing orders");
    }

    public void RecordOrderProcessed(string region, double durationMs)
    {
        // Tags enable dimensional filtering (by region, status, etc.)
        var tags = new TagList
        {
            { "region", region },
            { "order.type", "standard" }
        };

        _ordersProcessed.Add(1, tags);
        _orderProcessingDuration.Record(durationMs, tags);
    }
}
```

Register `OrderMetrics` in DI:

```csharp
builder.Services.AddSingleton<OrderMetrics>();
```

### Step 6: Configure context propagation for distributed scenarios

Trace context propagation is automatic for HTTP calls when using `AddHttpClientInstrumentation()`. For non-HTTP scenarios:

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using OpenTelemetry.Context.Propagation;

// ActivitySource should be static — register via .AddSource("MyApp.Messaging") in Step 2
private static readonly ActivitySource MessageSource = new("MyApp.Messaging");

// Manual context propagation (e.g., across message queues)
// On the SENDING side:
var propagator = Propagators.DefaultTextMapPropagator;
var activityContext = Activity.Current?.Context ?? default;
var context = new PropagationContext(activityContext, Baggage.Current);
var carrier = new Dictionary<string, string>();

propagator.Inject(context, carrier, (dict, key, value) => dict[key] = value);
// Send carrier dictionary as message headers

// On the RECEIVING side:
var parentContext = propagator.Extract(default, carrier,
    (dict, key) => dict.TryGetValue(key, out var value) ? new[] { value } : Array.Empty<string>());

Baggage.Current = parentContext.Baggage;
using var activity = MessageSource.StartActivity("ProcessMessage",
    ActivityKind.Consumer,
    parentContext.ActivityContext);  // Links to parent trace!
```

## Validation

- [ ] Traces appear in the observability backend (Jaeger, Aspire dashboard, etc.)
- [ ] HTTP requests automatically create spans with correct verb, URL, status code
- [ ] Custom `ActivitySource` names match `AddSource()` registrations
- [ ] Custom `Meter` names match `AddMeter()` registrations
- [ ] Logs include TraceId and SpanId for correlation
- [ ] Health check endpoints are filtered from traces
- [ ] Exception details appear on error spans

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| `ActivitySource.StartActivity` returns null | Source name doesn't match any `AddSource()` — names must match exactly |
| Traces not appearing in exporter | Check OTLP endpoint: gRPC uses port 4317, HTTP uses 4318 |
| Missing HTTP client spans | Ensure `AddHttpClientInstrumentation()` is registered; it works for both `IHttpClientFactory`/DI and `new HttpClient()` (use `IHttpClientFactory` for lifetime management) |
| High cardinality tags | Don't use user IDs, request IDs, or UUIDs as metric tags — explodes storage |
| OTLP gRPC vs HTTP mismatch | Default is gRPC (port 4317); if collector only accepts HTTP, set `OtlpExportProtocol.HttpProtobuf` |
| `Meter` / `ActivitySource` lifecycle | `ActivitySource` should be static; create `Meter` via `IMeterFactory` from DI (not `new Meter()`) for proper lifetime management and testability |
