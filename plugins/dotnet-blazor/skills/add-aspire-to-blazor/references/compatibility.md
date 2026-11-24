# Compatibility

## Released baseline

These are the versions used by the recipes, checked against public release and package
metadata on November 24, 2026.

| Component | Version |
|---|---|
| .NET SDK | `11.0.100` |
| ASP.NET Core runtime, Blazor packages, and Gateway CLI | `11.0.0` |
| Aspire CLI bundle, project templates, `Aspire.Hosting.AppHost`, and `Aspire.Hosting.Dotnet` | `17.0.0` |
| `Aspire.Hosting.Blazor` | `17.0.0-preview.1.26567.5` (prerelease; no stable package) |
| `Yarp.ReverseProxy` | `2.3.0` |
| `Microsoft.Extensions.ServiceDiscovery.Yarp` | `11.0.1` |

Use the [released .NET 11 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/11.0)
and the [Aspire CLI bundle](https://aspire.dev/get-started/install-cli/). On Aspire 17, new C#
AppHosts use `Microsoft.NET.Sdk` for project files, `Aspire.Hosting.AppHost` build targets, and
path-based registrations from `Aspire.Hosting.Dotnet`. Aspire 17's AppHost templates can target
`net10.0` while the Blazor projects target `net11.0`; keep the AppHost's compatible target and
ensure its runtime is installed. The CLI bundle is required for C# AppHost orchestration.
Use `aspire run`, not a bare `dotnet run`, as the validation entry point.

The browser recipes use the [experimental Blazor hosting integration](https://aspire.dev/integrations/dotnet/blazor-hosting/)
on that stable toolchain. Suppress `ASPIREBLAZOR001` only around its experimental calls. If the
project cannot take a preview dependency, use stable packages for server-only orchestration and
report that these browser recipes require the prerelease package.

Inspect `global.json`, target frameworks, package management, NuGet sources, `aspire --version`,
and `dotnet new list` before editing. Check the installed template's help before using
`blazorwasm-servicedefaults --hosted`. If the AppHost or server defaults template is missing,
install the matching templates with `dotnet new install Aspire.ProjectTemplates::17.0.0`.
Use `dotnet new aspire-apphost` for a project AppHost or `dotnet new aspire-apphost-singlefile`
for `apphost.cs`; use `dotnet new aspire-servicedefaults` for missing server defaults only.
Keep an existing compatible version set. For other versions, check matching documentation or
source, then restore, build, and run the relevant scenario.

## Dashboard endpoint troubleshooting

The browser sends OTLP/HTTP to the app's same-origin proxy, not to the dashboard's gRPC
endpoint. In Aspire 17 the proxy first looks for `otlp-http` on the dashboard resource.
The stock profiles specify gRPC only. If the gateway logs that it cannot resolve the
dashboard HTTP endpoint, or the effective browser config has no OTLP endpoint, configure
the HTTP endpoint explicitly. Setting `WithOtlpExporter` on the gateway resource itself
doesn't create the dashboard endpoint for the browser proxy.

If Aspire reports that no dashboard HTTP endpoint could be resolved, inspect how the dashboard
is started and whether it accepts OTLP/HTTP. For an external dashboard, use its configured HTTP
endpoint; the AppHost also recognizes `ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL` or
`DOTNET_DASHBOARD_OTLP_HTTP_ENDPOINT_URL` as a fallback. If an explicit setting is needed for a
local dashboard, put it in the active profile in `Properties/launchSettings.json` (project) or
`apphost.run.json` (file-based), alongside the existing gRPC setting, with a distinct port and
matching scheme. For example, in the HTTPS profile's `environmentVariables`:

```json
"ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL": "https://localhost:21242",
"ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL": "https://localhost:21243"
```

Keep the existing gRPC address instead of copying the example. Recheck browser configuration
and a successful export. Do not copy dashboard addresses, authentication headers, or login
tokens into browser configuration or output.

## General integration rules

Preserve working capabilities and project names. Distinguish standalone WASM hosting from a
Blazor Web App's server host; browser-only pieces do not belong in server-only apps. Keep API
addresses behind service discovery and a same-origin proxy. Verify browser telemetry separately
from server telemetry, using the configuration and network requests as evidence.

## Issues observed on the recorded preview builds

The July 2026 run used SDK `11.0.100-preview.7.26366.102`, ASP.NET Core
`11.0.0-preview.7.26366.102`, and Aspire CLI and hosting packages
`13.5.0-preview.1.26372.1`. These observations apply to that run, not to the released baseline.

| Observation | Action for the recorded build |
|---|---|
| Framework preview restore failed with `NU1102` because packages were absent from the configured sources. | Verify availability on the public `dotnet11` feed and source mappings before changing feeds; get agreement before adopting daily packages. |
| CLI `13.5.0-preview.1.26277.22` was older than hosting packages/DCP `26372.1`; gateway endpoint allocation failed. | Align CLI and hosting tooling. Do not update to today's daily channel as a generic fix. |
| A file-based standalone AppHost still failed gateway port allocation after aligning versions. | The project AppHost path ran successfully on that build. Reproduce on the selected version before treating it as a current restriction. |
| Standalone assets returned 404 when the base href remained `/`. | Match the base href to the WASM resource sub-path, such as `/app/`. |
| Gateway configuration contained discovery keys but no OTLP keys; browser telemetry was absent. | Set `ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL` alongside the gRPC setting if the HTTP endpoint is absent, then verify export rather than assuming it worked. |
| Direct HTTP/1.1 requests to standalone `/app/` paths returned `500` with `AmbiguousMatchException`, while a browser using HTTP/2 rendered the app. | Compare browser and direct navigation/probe results and report the routing defect; do not hide it with retries or claim all routes pass. |

The original clean standalone run verified rendering, API proxying, and server telemetry, but
browser telemetry was partial. The original hosted run verified server-side behavior, not the
later browser-proxy recipe. Treat the recipes as instructions to validate, not as evidence that
every depicted configuration already passed end-to-end.

## Reporting

Record the versions actually used and separate compilation, app rendering, API access, server
telemetry, and browser logs/traces/metrics. If tooling, feeds, dashboard access, or a product defect
prevents a check, report that limitation. Do not generalize historical failures or successes to a
new release without reproducing them.
