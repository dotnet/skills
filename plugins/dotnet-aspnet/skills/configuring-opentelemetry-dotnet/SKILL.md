---
name: configuring-opentelemetry-dotnet
description: Configure OpenTelemetry distributed tracing, metrics, and logging in ASP.NET Core using the .NET OpenTelemetry SDK. Use when adding observability, setting up OTLP exporters, creating custom metrics/spans, or troubleshooting distributed trace correlation.
---

# Configuring OpenTelemetry in .NET

## When to Use

- Adding distributed tracing to an ASP.NET Core application
- Setting up OpenTelemetry exporters (OTLP is the primary protocol shown; Jaeger and Prometheus accept OTLP natively)
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

# Exporter (pick one or more — also used by logging in Step 3)
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol  # OTLP exporter for traces, metrics, AND logs
dotnet add package OpenTelemetry.Exporter.Console                # Dev/debugging
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

### Step 2: Configure tracing and metrics in Program.cs

```csharp
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using OpenTelemetry.Logs;
using OpenTelemetry.Exporter;  // for OtlpExportProtocol

var builder = WebApplication.CreateBuilder(args);

// Define the service resource (appears in all telemetry)
var serviceName = "MyOrderService";
var serviceVersion = "1.0.0";

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(serviceName: serviceName, serviceVersion: serviceVersion))
    .WithTracing(tracing => tracing
        // Auto-instrumentation sources
        .AddAspNetCoreInstrumentation(options =>
        {
            // Filter out health check endpoints from traces
            options.Filter = httpContext =>
                !httpContext.Request.Path.StartsWithSegments("/healthz");
        })
        .AddHttpClientInstrumentation(options =>
        {
            // Enrich outgoing HTTP spans with request/response details
            options.RecordException = true;
        })
        // Optional: add SQL instrumentation if using SqlClient directly
        // .AddSqlClientInstrumentation(options =>
        // {
        //     options.SetDbStatementForText = true;
        //     options.RecordException = true;
        // })
        // Custom activity sources (for your own spans)
        .AddSource("MyOrderService.Orders")
        .AddSource("MyOrderService.Payments")
        // Exporter
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri("http://localhost:4317"); // gRPC endpoint
            // For HTTP: options.Protocol = OtlpExportProtocol.HttpProtobuf;
            //           options.Endpoint = new Uri("http://localhost:4318/v1/traces");
        }))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        // Optional: .AddRuntimeInstrumentation() for GC and thread pool metrics
        //   (requires OpenTelemetry.Instrumentation.Runtime package)
        // Custom meters
        .AddMeter("MyOrderService.Metrics")
        // Note: Jaeger is traces-only. For metrics, export to an OTel Collector or
        // Prometheus-compatible backend. Here we use the same OTLP endpoint assuming
        // an OTel Collector that routes traces and metrics to appropriate backends.
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri("http://localhost:4317"); // gRPC endpoint (metrics)
        }));
```

### Step 3: Add OpenTelemetry logging integration

No additional packages are needed — `AddOpenTelemetry()` comes from the `OpenTelemetry` package (a transitive dependency of `OpenTelemetry.Extensions.Hosting`), and `AddOtlpExporter()` comes from `OpenTelemetry.Exporter.OpenTelemetryProtocol`, both already installed in Step 1.

```csharp
// Connect ILogger to OpenTelemetry
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeScopes = true;
    logging.IncludeFormattedMessage = true;
    logging.AddOtlpExporter(options =>
    {
        options.Endpoint = new Uri("http://localhost:4317");
    });
});
```

**This correlates logs with traces automatically** — each log entry gets the current TraceId and SpanId.

### Step 4: Create custom spans (Activities) for business operations

```csharp
using System.Diagnostics;
using OpenTelemetry.Trace;

public class OrderService
{
    // Create an ActivitySource matching what you registered in Step 2
    private static readonly ActivitySource ActivitySource = new("MyOrderService.Orders");

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
            // Record the exception on the span
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);
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
using Microsoft.Extensions.Diagnostics.Metrics;

public class OrderMetrics
{
    // Meter name must match AddMeter("...") in configuration
    private readonly Counter<long> _ordersProcessed;
    private readonly Histogram<double> _orderProcessingDuration;
    private readonly UpDownCounter<int> _activeOrders;

    public OrderMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("MyOrderService.Metrics");

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

// ActivitySource should be static — register via .AddSource("MyOrderService.Messaging") in Step 2
private static readonly ActivitySource MessageSource = new("MyOrderService.Messaging");

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
| Static `Meter` instead of `IMeterFactory` | Prefer `IMeterFactory` from DI for proper lifetime management and testability |
| `Meter` and `ActivitySource` not static | `ActivitySource` should be static; `Meter` should be created via `IMeterFactory` in DI |
