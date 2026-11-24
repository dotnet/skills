---
license: MIT
name: add-aspire-to-blazor
description: >-
  Add or complete .NET Aspire integration for an existing Blazor app: service discovery,
  same-origin API proxying, and server/browser OpenTelemetry in the Aspire dashboard.
  USE FOR: "add aspire to my blazor app", "finish wiring aspire", standalone Blazor
  WebAssembly with a Blazor Gateway, hosted Blazor Web Apps (Server/WebAssembly/Auto),
  and missing browser logs, traces, or metrics in an Aspire setup.
  DO NOT USE FOR: creating a new Blazor app (use create-blazor-project), general HTTP
  data-loading patterns (use fetch-and-send-data), non-Blazor apps, Blazor Hybrid/MAUI,
  Azure/Kubernetes deployment, or AppHost databases/containers/messaging unrelated to
  the Blazor front end.
compatibility: .NET 11 SDK and Aspire 17 CLI; the browser recipes require the prerelease Aspire.Hosting.Blazor integration.
---

# Add .NET Aspire to a Blazor App

Connect an existing Blazor front end to backend services through **Aspire service discovery** and stream its telemetry — including **browser telemetry** from a WebAssembly client — to the **Aspire dashboard**.

## Compatibility

The recipes target **.NET 11 RTM and Aspire 17.0**. The Aspire CLI and core hosting packages
are stable, but `Aspire.Hosting.Blazor` is still prerelease. Check your resolved versions against
[compatibility.md](references/compatibility.md) before adding packages. If the project requires
stable-only dependencies, don't add the Blazor hosting integration; report which browser
capabilities are blocked. A server-only Blazor Web App can use the stable Aspire packages alone.
Preserve working versions and do not upgrade projects or change package sources without agreement.

Follow the workflow in order: analyze what the solution already has, identify which Blazor scenario it is, list the capabilities it still needs, add them, and validate against the dashboard. Keep whatever already works and add only the capabilities that are missing.

## Workflow

### Step 1 — Analyze the current solution

Walk the solution and record which capabilities are already present. Run each check; the results drive Steps 3–4.

| Capability | Present when |
|---|---|
| **Orchestrator (AppHost)** | a project or file-based app references `Aspire.Hosting.AppHost` or uses the legacy `Aspire.AppHost.Sdk`. Look for `DistributedApplication.CreateBuilder` and its package references or directives. |
| **Server service defaults** | each server/API project calls `AddServiceDefaults()` and `MapDefaultEndpoints()`. |
| **Client service defaults** | a WebAssembly library contains `AddBlazorClientServiceDefaults()`, the WASM project references it, and startup calls it. Do not rely on the library's name. |
| **Backend API** | the solution owns an API project or calls an external service. Service discovery and the API proxy apply only when an API exists. |
| **Service discovery to the API** | each applicable server/browser `HttpClient` uses `BaseAddress = new Uri("https+http://<name>")` and the AppHost `WithReference`s that API resource. |
| **Same-origin API proxy** | the WASM client reaches the API through the gateway/server on a relative path. |
| **Browser telemetry export** | the dashboard exposes an HTTP/protobuf OTLP endpoint and the OTLP + discovery config reaches the client. |

### Step 2 — Identify the concrete scenario

Match the front end to one of two scenarios; each wires the browser differently. Open the matching reference and work from it in Step 4.
Resolve references relative to this `SKILL.md`. If a bundled reference is missing, list the known
`references` directory once and report the missing coverage; do not search for an installation.

| The solution has | Scenario | Reference |
|---|---|---|
| A `Microsoft.NET.Sdk.BlazorWebAssembly` project with `wwwroot/index.html` and no server host | **Standalone Blazor WebAssembly + Blazor Gateway** — a gateway serves the WASM files and injects config via `/_blazor/_configuration` | [standalone-gateway.md](references/standalone-gateway.md) |
| A `Microsoft.NET.Sdk.Web` server calling `AddRazorComponents()` (interactivity Server, WebAssembly, or Auto), optionally with a `.Client` project | **Hosted Blazor Web App** — the server hosts a YARP proxy and pushes config to the browser | [hosted-webapp.md](references/hosted-webapp.md) |

For static SSR or Interactive Server with no WebAssembly execution, add only the AppHost,
server/API service defaults, and server-side discovery. Do not create a `.Client` project,
gateway, YARP browser proxy, or browser telemetry pipeline just to add Aspire.

### Step 3 — Identify the necessary steps

The capabilities that Step 1 found missing, filtered to those the scenario's reference lists, are your task list. Three facts shape that list:

- **Preserve the AppHost format.** On Aspire 17, both project-based and file-based C# AppHosts
  can register .NET services by path with `AddDotnetProject` and standalone clients with
  `AddBlazorWasmApp`. Resolve paths from the AppHost directory. Retained legacy registrations may
  still use generated `Projects.*` types; don't remove their references or migrate unrelated services.
- **The browser exports through an HTTP OTLP proxy.** OpenTelemetry Protocol (OTLP) browser
  export uses the app's origin and the dashboard's HTTP/protobuf endpoint. Aspire 17 looks for
  the dashboard endpoint in its resource model first. The stock launch profiles only specify
  gRPC; if the proxy can't resolve HTTP, configure it explicitly in the active profile.
  Don't expose the dashboard URL or credentials to browser code.
- **Service discovery replaces literal URLs.** The client reaches the API by resource name (`https+http://<name>`), proxied same-origin, so the API's host and port stay out of client code.

### Step 4 — Perform the steps

Work through the missing capabilities in the scenario's reference: service defaults, service
discovery, AppHost wiring, then the effective dashboard HTTP endpoint. Check each before editing.

Prerequisites: a **.NET 11 SDK**, the **Aspire CLI bundle** (`aspire --version`), and matching
hosting packages and templates. Use `dotnet new blazorwasm-servicedefaults --help` to verify the
client template is available. If there is no AppHost, use an Aspire 17 C# AppHost template.

For each API and hosted server missing service defaults, generate an `aspire-servicedefaults`
library using the compatible installed template, reference it from those projects, call
`builder.AddServiceDefaults()` before `Build()`, and `app.MapDefaultEndpoints()` before `Run()`.
Reuse existing defaults and do not add server defaults to a WebAssembly project.
If all applicable capabilities are already present, leave the files unchanged and validate them.

### Step 5 — Validate the changes

Run the orchestrator with the `aspire` CLI from the AppHost's directory, using its existing
HTTPS launch profile:

```bash
aspire run
```

Only when deliberately testing local HTTP endpoints, opt into unsecured transport for that
process (`$env:ASPIRE_ALLOW_UNSECURED_TRANSPORT = 'true'` in PowerShell); do not persist it
as a deployment setting. Use `aspire run` for either C# AppHost format so it can resolve the
CLI bundle's dashboard and orchestration dependencies.

The console prints a **dashboard URL with a `?t=<token>` login link**. Open it and confirm each capability you wired:

1. **Resources running** — every resource (Blazor app, API if present, `gateway` on the standalone path) is **Running**.
2. **App renders** — open the app's endpoint. On the standalone path use the WASM resource's sub-path (for example `/app/`); a matching `<base href>` keeps its assets loading.
3. **Service discovery** — a client call to the API returns data. When it runs in the browser,
   verify that the request reaches the gateway/server on the app's origin.
4. **Server telemetry** — Structured Logs, Traces, and Metrics show the server resources, and Traces shows one distributed trace from the front end to the API.
5. **Browser telemetry** — verify successful browser OTLP posts and matching dashboard logs,
   traces, and metrics. Trigger an interaction or API call and leave the page open through
   the export interval; logs and traces can arrive before metrics.

The integration is complete when the checks that apply to the solution pass: the app renders, the client reaches the API by name same-origin, one trace spans front end to API, and the WASM client reports browser telemetry.
Skip API checks when there is no API, and browser checks when there is no WebAssembly execution.
Report which checks passed, failed, or could not run. A successful build or server-only trace does
not prove browser telemetry; do not report unobserved dashboard signals as a pass.

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Blank page or assets 404 on the standalone path | `<base href>` doesn't match the gateway sub-path | Set `<base href="/app/" />` to the WASM resource name |
| Client config has discovery keys but no OTLP keys | HTTP OTLP endpoint or browser telemetry proxy was not resolved | Check the selected AppHost's telemetry proxy setup and dashboard HTTP/protobuf endpoint before changing client code |
| `Projects.*` type not found | The AppHost doesn't generate legacy project metadata | On Aspire 17, use `AddDotnetProject("name", "path")` or `AddBlazorWasmApp("name", "path")` for the new resource |
| Client call goes cross-origin | `HttpClient` targets a literal host:port | Point it at `https+http://<name>` and let the gateway/server proxy it |
| Browser metrics missing while logs and traces show | browser metrics flush on a ~60-second timer and reset on navigation | Stay on one page ~70 seconds |
