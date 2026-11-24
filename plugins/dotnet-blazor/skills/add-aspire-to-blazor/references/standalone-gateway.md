# Standalone Blazor WebAssembly + Blazor Gateway

The snippets use [.NET 11 RTM and Aspire 17](compatibility.md). The Blazor AppHost integration
remains experimental.

For a **standalone Blazor WebAssembly** app (SDK `Microsoft.NET.Sdk.BlazorWebAssembly`, `wwwroot/index.html`, no server host). A **Blazor Gateway** resource serves the WASM static files, proxies the app's API calls by service discovery, and injects OpenTelemetry config so the **browser** exports logs/traces/metrics to the dashboard. The gateway serves the app under a **sub-path named after the WASM resource** (resource `app` → `/app/`).

Work through the capabilities below. For each, check whether the solution already has it; add only what's missing. The pieces the browser relies on — the gateway's config injection via `/_blazor/_configuration`, the API proxy, and the OTLP endpoint — all come from the gateway resource in the AppHost.

Project and resource names here (`Client`, `ApiService`, `ClientServiceDefaults`, the `app`/`gateway`/`your-api` resources) are illustrative — map them to the solution's actual names and paths.
Do not treat the gateway route as an access control boundary. Verify that protected APIs
still reject unauthorized requests through the gateway before exposing it.

## Contents
- [Client service defaults](#client-service-defaults)
- [Gateway integration on the WASM project](#gateway-integration-on-the-wasm-project)
- [The base href sub-path](#the-base-href-sub-path)
- [Service discovery to the API](#service-discovery-to-the-api)
- [The AppHost: WASM project + gateway](#the-apphost-wasm-project--gateway)
- [The dashboard's HTTP OTLP endpoint](#the-dashboards-http-otlp-endpoint)
- [Validate](#validate)

## Client service defaults

**Present when** a `*-servicedefaults` WebAssembly library exists, the WASM project references it, and startup calls `AddBlazorClientServiceDefaults()`.

**Add it** by generating the library, referencing it, and calling it. The .NET 11 template emits
`AddBlazorClientServiceDefaults()` in `Microsoft.Extensions.Hosting` and a
`wwwroot/*.lib.module.js` initializer that reads `_blazor/_configuration` relative to the app's
base path. Leave both files as generated.

```bash
dotnet new blazorwasm-servicedefaults -o ClientServiceDefaults
dotnet add Client/Client.csproj reference ClientServiceDefaults/ClientServiceDefaults.csproj
```

In the WASM app's `Program.cs`, bridge the gateway-injected environment variables into configuration, then add the defaults:

```csharp
using Microsoft.Extensions.Hosting;

// Bridge env vars (injected by the gateway via /_blazor/_configuration) into IConfiguration
// so service discovery reads services__your-api__* and telemetry reads OTEL_*.
builder.Configuration.AddEnvironmentVariables();

// Aspire client service defaults: OpenTelemetry (logs/traces/metrics) + service discovery.
builder.AddBlazorClientServiceDefaults();
```

The generated defaults already wire component instrumentation. The .NET 11 WASM host starts
registered hosted services, so do not manually resolve telemetry providers or recreate the
preview's `BackgroundExportHandler`.

## Gateway integration on the WASM project

**Present when** the WASM `.csproj` references `Microsoft.AspNetCore.Components.Gateway` and sets the static-web-asset SPA-fallback properties.

**Add it** only if missing. The .NET 11 standalone template already enables SPA fallback.
If the project still references `Microsoft.AspNetCore.Components.WebAssembly.DevServer`,
replace that development server reference with `Microsoft.AspNetCore.Components.Gateway`;
do not register both. The client service defaults supply the configuration initializer.

```xml
<PropertyGroup>
  <OverrideHtmlAssetPlaceholders>true</OverrideHtmlAssetPlaceholders>
  <StaticWebAssetSpaFallbackEnabled>true</StaticWebAssetSpaFallbackEnabled>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="Microsoft.AspNetCore.Components.Gateway" Version="11.0.0" PrivateAssets="all" />
</ItemGroup>
```

## The base href sub-path

**Present when** `wwwroot/index.html` has `<base href="/app/" />` matching the WASM resource name in the AppHost.

The gateway serves the app under `/app/`, but a standalone `blazorwasm` project ships `<base href="/" />`. Left at `/`, every relative asset URL resolves to the site root and 404s (blank page / endless spinner). The tooling does not rewrite this — set it to the resource sub-path yourself:

```html
<base href="/app/" />
```

The sub-path must equal the WASM resource name you give in the AppHost. Keep the two in sync.

## Service discovery to the API

Only applies if the app calls an API. **Present when** the client's `HttpClient` targets `https+http://<name>` and the AppHost references that API resource — no hardcoded host:port.

**Add it** by registering a named client against the resource name and pointing your components at it:

```csharp
builder.Services.AddHttpClient("your-api", client =>
{
    client.BaseAddress = new Uri("https+http://your-api");
});
```

Inject `IHttpClientFactory` and call `CreateClient("your-api")` instead of embedding a backend
URL. Service discovery resolves `https+http://your-api` to the gateway route under
`/app/_api/your-api/`. Use the actual AppHost resource name everywhere.

## The AppHost: WASM project + gateway

**Present when** the AppHost declares the WASM app, binds its gateway resource with
`WithBlazorClientApp`, and (if there's an API) the app references it with `WithReference`.

Use whichever AppHost form the solution already has. Both Aspire 17 C# forms use paths relative
to the AppHost directory. The old `AddBlazorGateway` registers a legacy project resource; use
`AddDotnetProjectBlazorGateway` for new registrations.

**Project AppHost** — add the package references to the project's existing configuration.
The `net10.0` AppHost can orchestrate `net11.0` applications:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Aspire.Hosting.AppHost" Version="17.0.0" />
    <PackageReference Include="Aspire.Hosting.Dotnet" Version="17.0.0" />
    <PackageReference Include="Aspire.Hosting.Blazor" Version="17.0.0-preview.1.26567.5" />
  </ItemGroup>
</Project>
```

**File-based AppHost** — put the package directives at the top of `apphost.cs` instead:

```csharp
#:package Aspire.Hosting.AppHost@17.0.0
#:package Aspire.Hosting.Dotnet@17.0.0
#:package Aspire.Hosting.Blazor@17.0.0-preview.1.26567.5
```

In either form, register the backend, app, and gateway. Ensure the dashboard HTTP endpoint
is available before selecting HTTP/protobuf for the gateway's own export. Adjust paths to match
the solution; this example places the AppHost in a sibling directory:

```csharp
using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddDotnetProject("your-api", "../ApiService/ApiService.csproj");

#pragma warning disable ASPIREBLAZOR001
var blazorApp = builder.AddBlazorWasmApp("app", "../Client/Client.csproj")
    .WithReference(apiService);

builder.AddDotnetProjectBlazorGateway("gateway")
    .WithExternalHttpEndpoints()
    .WithOtlpExporter(OtlpProtocol.HttpProtobuf)
    .WithBlazorClientApp(blazorApp);
#pragma warning restore ASPIREBLAZOR001

builder.Build().Run();
```

The WASM resource name (`app`) determines the URL sub-path; keep it aligned with the base
href. `WithReference(apiService)` adds discovery keys and the same-origin API proxy route
under `/app/_api/your-api/`. `WithBlazorClientApp` enables browser telemetry proxying by
default; the dashboard must expose OTLP/HTTP for Aspire to populate the browser's `OTEL_*`
keys. `WithOtlpExporter` configures the gateway process's own export, not the browser proxy.
If there's no API, omit its registration and `WithReference` call.

On `aspire run`, this gateway launches the .NET 11 Gateway CLI through `dotnet tool exec`.
The first run needs a NuGet source containing `Microsoft.AspNetCore.Components.Gateway.Cli`
`11.0.0`. A restore or tool-acquisition failure is not an API or routing failure.

## The dashboard's HTTP OTLP endpoint

**Present when** the effective dashboard model exposes an HTTP/protobuf OTLP endpoint and
the gateway configuration includes the relative `OTEL_EXPORTER_OTLP_ENDPOINT`.

Check the injected configuration and actual export before editing launch settings. If the
AppHost cannot resolve an HTTP endpoint, follow [Dashboard endpoint troubleshooting](compatibility.md#dashboard-endpoint-troubleshooting).

## Validate

Run the AppHost, open the dashboard from the printed `?t=<token>` link, and check the pieces you wired:

1. **Resources** — the WASM `app`, `gateway`, and the API (if present) all **Running**.
2. **App** — open the gateway's HTTPS endpoint at **`/app/`** and a direct client route
   under it. Confirm the app renders and its assets load under the same prefix.
3. **Service discovery** — trigger the client's API call; the gateway proxies
   `GET /app/_api/your-api/… → 200`. Use the gateway's actual HTTPS port to inspect the
   injected discovery and OTLP keys:
   ```bash
   curl https://localhost:7123/app/_blazor/_configuration
   # services__your-api__https__0 / __http__0
   # OTEL_SERVICE_NAME / OTEL_EXPORTER_OTLP_ENDPOINT / OTEL_EXPORTER_OTLP_PROTOCOL
   ```
   Missing `OTEL_*` keys mean the browser export path still needs checking. Ensure the response
   does not expose `OTEL_EXPORTER_OTLP_HEADERS` or dashboard credentials.
4. **Distributed trace** — trigger an API call in the browser and inspect the trace for
   the WASM client, gateway, and API spans.
5. **Browser telemetry** — trigger an interaction and check browser network requests to
   `/app/_otlp/v1/{logs,traces,metrics}`. Confirm matching signals under the WASM app resource in
   the dashboard. Wait through the metrics export interval before marking metrics missing.
