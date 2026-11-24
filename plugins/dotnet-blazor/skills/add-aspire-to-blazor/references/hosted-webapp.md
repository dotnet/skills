# Hosted Blazor Web App + Aspire (server-hosted YARP + browser telemetry)

The snippets use [.NET 11 RTM and Aspire 17](compatibility.md). The Blazor AppHost integration
remains experimental.

For a **Blazor Web App** (`Microsoft.NET.Sdk.Web` server calling `AddRazorComponents()`, any interactivity: Server, WebAssembly, or Auto). The web app joins the AppHost as a **server project**. On this path the **server** reaches the API and pushes configuration to the browser: it hosts a **YARP reverse proxy** (bound from config the AppHost emits) so the interactive-WASM client reaches the API and the OTLP dashboard **same-origin**, and it pushes service-discovery + OTLP config to the client with `WithBrowserOptions` (delivered via the Blazor-Configuration DOM comment — no `/_blazor/_configuration` endpoint and no gateway).

Work through the capabilities below and add only what the solution is missing. The moving parts that make browser telemetry work here are the AppHost's `ProxyBlazorService`/`ProxyBlazorTelemetry` calls, the server's YARP proxy bound from the host-emitted config, and the server's `WithBrowserOptions` push.

For static SSR or Interactive Server without WebAssembly execution, skip the client defaults,
YARP browser proxy, browser configuration push, and browser telemetry sections. Register the server
with the AppHost and add server/API service defaults and discovery only. Do not add a `.Client`
project or change render modes. For mixed-mode apps, preserve the existing render-mode registrations
and apply browser pieces only to the WebAssembly path; the code below illustrates both modes.
Keep existing middleware, authentication/authorization, antiforgery, and endpoint behavior when
merging the examples; do not replace the entire startup file.
Decide how each proxied API is authorized before exposing it. YARP routes do not inherit
authorization from Blazor pages. If the front end enforces access control, configure the
appropriate YARP `AuthorizationPolicy` on the generated API route and verify that an
unauthenticated request is rejected. Same-origin routing does not enforce authorization.

Project and resource names here (`WebApp`/`WebApp.Client`, `ClientServiceDefaults`, `your-api`, `webapp`) are illustrative — map them to the solution's actual names and paths.

## Contents
- [Client service defaults (hosted mode)](#client-service-defaults-hosted-mode)
- [Server: YARP proxy + browser config push](#server-yarp-proxy--browser-config-push)
- [Service discovery to the API](#service-discovery-to-the-api)
- [The AppHost: proxy the API and its telemetry](#the-apphost-proxy-the-api-and-its-telemetry)
- [Browser telemetry: the HTTP OTLP endpoint](#the-dashboards-http-otlp-endpoint)
- [Validate](#validate)

## Client service defaults (hosted mode)

**Present when** a `*-servicedefaults` WebAssembly library generated in **hosted mode** exists, the `.Client` project references it, and its `Program.cs` calls `AddBlazorClientServiceDefaults()`.

**Add it** with `dotnet new blazorwasm-servicedefaults --hosted`. Hosted configuration flows
from the server via the Blazor-Configuration DOM comment. The standalone variant adds a JS
initializer that fetches `/_blazor/_configuration`; do not reuse it here. Leave the generated
`AddBlazorClientServiceDefaults()` extension in `Microsoft.Extensions.Hosting` as generated.

```bash
dotnet new blazorwasm-servicedefaults --hosted -o ClientServiceDefaults
dotnet add WebApp/WebApp.Client/WebApp.Client.csproj reference ClientServiceDefaults/ClientServiceDefaults.csproj
```

In `WebApp.Client/Program.cs`, surface the host-pushed env vars into configuration and add the defaults:

```csharp
using Microsoft.Extensions.Hosting;

// The host pushes browser config (service discovery + OTLP endpoint) via WithBrowserOptions,
// delivered as environment variables. Service discovery and telemetry read IConfiguration.
builder.Configuration.AddEnvironmentVariables();

// Aspire client service defaults: OpenTelemetry (logs/metrics/traces) + service discovery.
builder.AddBlazorClientServiceDefaults();
```

The generated defaults already wire component instrumentation. The .NET 11 WASM host starts
registered hosted services, so do not manually resolve telemetry providers or recreate the
preview's `BackgroundExportHandler`.

## Server: YARP proxy + browser config push

This is the piece unique to the hosted path, and the one most likely missing. **Present when** the server registers `AddReverseProxy().LoadFromConfig(...).AddServiceDiscoveryDestinationResolver()`, calls `MapReverseProxy()`, and pushes browser config with `MapRazorComponents<App>()…WithBrowserOptions(...)`.

**Server `.csproj`** — reference `ServiceDefaults`, add the YARP packages, and keep the template's `Components.WebAssembly.Server` reference:

```xml
<ItemGroup>
  <ProjectReference Include="..\WebApp.Client\WebApp.Client.csproj" />
  <PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly.Server" Version="11.0.0" />
  <!-- YARP bound from the host-emitted ReverseProxy config, with service discovery resolution. -->
  <PackageReference Include="Yarp.ReverseProxy" Version="2.3.0" />
  <PackageReference Include="Microsoft.Extensions.ServiceDiscovery.Yarp" Version="11.0.1" />
</ItemGroup>
<ItemGroup>
  <ProjectReference Include="..\..\ServiceDefaults\ServiceDefaults.csproj" />
</ItemGroup>
```

**Server `Program.cs`** — merge these changes into the existing startup file. Register YARP
from the AppHost's `ReverseProxy` configuration, map it only when registered, then pass the
AppHost's `Client:ConfigResponse` environment values to the browser. Preserve the existing
HTTPS and middleware policy:

```csharp
using System.Text.Json;
using WebApp.Components;

var builder = WebApplication.CreateBuilder(args);

// Aspire service defaults: OpenTelemetry, health checks, service discovery, resilience.
builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

// YARP reverse proxy so the WASM client reaches the API and the OTLP dashboard same-origin.
// Bound from the "ReverseProxy" config emitted by ProxyBlazorService / ProxyBlazorTelemetry.
var reverseProxy = builder.Configuration.GetSection("ReverseProxy");
if (reverseProxy.Exists())
{
    builder.Services.AddReverseProxy()
        .LoadFromConfig(reverseProxy)
        .AddServiceDiscoveryDestinationResolver();
}

var app = builder.Build();

app.MapDefaultEndpoints();       // /health and /alive

// Keep existing error handling, HTTPS, and antiforgery middleware here.
app.MapStaticAssets();

if (reverseProxy.Exists())
{
    app.MapReverseProxy();
}

// Parse the host-emitted "Client:ConfigResponse" JSON ({ "webAssembly": { "environment": {...} } }).
// A literal "__ORIGIN__/" prefix becomes a same-origin path.
var browserEnvironment = ParseBrowserEnvironment(app.Configuration["Client:ConfigResponse"]);

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(WebApp.Client._Imports).Assembly)
    // Push the host-emitted browser configuration (service discovery + OTLP endpoint) to the WASM
    // client as environment variables, delivered via the Blazor-Configuration DOM comment.
    .WithBrowserOptions(options =>
    {
        foreach (var (key, value) in browserEnvironment)
        {
            options.InteractiveWebAssembly.EnvironmentVariables[key] = value;
        }
    });

app.Run();

static IReadOnlyDictionary<string, string> ParseBrowserEnvironment(string? configResponse)
{
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    if (string.IsNullOrEmpty(configResponse))
    {
        return result;
    }

    using var document = JsonDocument.Parse(configResponse);
    if (document.RootElement.TryGetProperty("webAssembly", out var webAssembly) &&
        webAssembly.TryGetProperty("environment", out var environment))
    {
        foreach (var item in environment.EnumerateObject())
        {
            if (item.NameEquals("OTEL_EXPORTER_OTLP_HEADERS"))
            {
                continue;
            }

            var value = item.Value.GetString()
                ?? throw new InvalidOperationException($"Browser setting '{item.Name}' must be a string.");
            const string originToken = "__ORIGIN__";
            result[item.Name] = value.StartsWith(originToken + "/", StringComparison.Ordinal)
                ? value[originToken.Length..]
                : value;
        }
    }

    return result;
}
```

`WithBrowserOptions` (namespace `Microsoft.AspNetCore.Builder`) takes an `Action<BrowserOptions>`.
The .NET 11 RTM member is `InteractiveWebAssembly.EnvironmentVariables`; only the incoming
Aspire JSON still uses `webAssembly.environment`. Don't add the obsolete
`UseWebAssemblyDebugging()` middleware or `inspectUri` settings from older templates.

Do not pass dashboard headers or secrets to the client. If the traces show that forwarding
OTLP causes repeated telemetry traffic, exclude only the confirmed telemetry ingress and
dashboard destination from instrumentation. Compose existing filters; don't drop all `/v1/`
requests, which could hide real API traces, or weaken HTTPS.

## Service discovery to the API

Only applies if the app calls an API. **Present when** the server's render-path client and the WASM client both target `https+http://<name>` — no hardcoded host:port.

For the server render path, register a typed or named client against the resource name:

```csharp
builder.Services.AddHttpClient<ApiClient>(client =>
{
    client.BaseAddress = new Uri("https+http://your-api");   // ApiClient stands in for your API's typed client
});
```

For the WASM client, register the named client in `WebApp.Client/Program.cs`; the host-pushed `services__your-api__https__0` value points it at the server's same-origin YARP route:

```csharp
builder.Services.AddHttpClient("your-api", client =>
{
    client.BaseAddress = new Uri("https+http://your-api");
});
```

Have components resolve these clients rather than literal URLs. `your-api` stands for whatever the API resource is named in the AppHost.

## The AppHost: proxy the API and its telemetry

**Present when** the server project is registered in the AppHost, references the API, and calls
`ProxyBlazorService(api)` and `ProxyBlazorTelemetry()` for its browser client.

Use the existing project or file-based AppHost with the
[Aspire 17 package references or directives](standalone-gateway.md#the-apphost-wasm-project--gateway).
Register the **server** project, not its `.Client` project. Resolve paths from the AppHost
directory, and suppress `ASPIREBLAZOR001` only around the experimental proxy calls:

```csharp
using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddDotnetProject("your-api", "../ApiService/ApiService.csproj");

var webApp = builder.AddDotnetProject("webapp", "../WebApp/WebApp/WebApp.csproj")
    .WithExternalHttpEndpoints()
    .WithReference(apiService);         // server-side service discovery (prerender)

#pragma warning disable ASPIREBLAZOR001
webApp.ProxyBlazorService(apiService)
    .ProxyBlazorTelemetry();
#pragma warning restore ASPIREBLAZOR001

builder.Build().Run();
```

`ProxyBlazorService` and `ProxyBlazorTelemetry` emit `ReverseProxy` config and a
`Client:ConfigResponse` JSON payload. Aspire resolves a browser-facing API route in local runs;
open the AppHost-advertised HTTPS URL for same-origin requests. If you use an alias or
forwarded URL, verify the resolved API origin matches the browser origin before claiming
the route is same-origin.

Add a health probe and startup wait only if the selected API profile serves the readiness
endpoint. The default `/health` endpoint from ServiceDefaults is development-only.

The browser's OTLP endpoint is relative (`/_otlp`). Deliver only the browser configuration via
`WithBrowserOptions`; YARP forwards requests to their destinations. If there is no API, omit its
registration, `WithReference`, and `ProxyBlazorService`, but keep
`webApp.ProxyBlazorTelemetry()` when browser telemetry is wanted.

## The dashboard's HTTP OTLP endpoint

Check the browser configuration for `OTEL_EXPORTER_OTLP_ENDPOINT=/_otlp`. If it is missing,
verify `ProxyBlazorTelemetry()` and follow
[Dashboard endpoint troubleshooting](compatibility.md#dashboard-endpoint-troubleshooting).

## Validate

Run `aspire run`, then open the dashboard from the printed `?t=<token>` link and check the
pieces you wired:

1. **Resources** — `webapp` and the API (if present) both **Running**.
2. **App** — open the `webapp` endpoint; the home page renders directly (no sub-path, no base-href edit on this path).
3. **Service discovery (server)** — open a server-rendered page that calls the API; the data fills. The webapp **Structured Logs** show `Start processing HTTP request GET https+http://your-api/… → 200`.
4. **Service discovery (browser)** — open an interactive-WASM page that calls the API; the data fills, and the browser network tab shows `GET /_api/your-api/… → 200` (proxied same-origin through YARP).
5. **Server traces + metrics** — one distributed trace `webapp → your-api`; both resources expose the standard instruments.
6. **Browser telemetry** — verify successful `POST /_otlp/v1/{logs,traces,metrics}` from a
   browser actually running the WASM client. On Aspire 17 the client source is named
   `webapp (client)`, separate from the server. Confirm matching entries in the dashboard and
   leave the page open through the metric export interval.
