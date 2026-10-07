# Render Modes Reference

Toolkit components need an interactive render mode whenever they handle clicks, bindings, dialogs, calendar navigation, or any other client interaction. A Toolkit component rendered in static SSR is display-only.

> **Framework support.** `Syncfusion.Blazor.Toolkit` 1.0.2 targets `net8.0`, `net9.0`, and `net10.0`. Always check the project's `<TargetFramework>` before installation. On `net6.0`, `net7.0`, or older frameworks, the package may restore successfully, but Toolkit assemblies are not available to the application, so the build later fails with errors such as `CS0246: The type or namespace name 'Syncfusion' could not be found`. Do not proceed with Toolkit installation on unsupported frameworks. Recommend upgrading the application to .NET 8 or later first, then continue with the installation steps.

> **Namespaces.** `SfButton` lives in `Syncfusion.Blazor.Toolkit.Buttons`, so each sample below includes `@using Syncfusion.Blazor.Toolkit.Buttons`. If it is already in `_Imports.razor`, the per-page line can be dropped.

Toolkit itself only needs `AddSyncfusionBlazorToolkit()`. Keep every `AddInteractive*` and `AddAdditionalAssemblies(...)` call exactly as the project template generated it; do not add or remove them for Toolkit.

## Template matrix (verified with `dotnet new blazor`)

`dotnet new blazor -int <mode>` makes the **server project** interactive-capable but leaves pages **static** until a page opts in. Adding `-ai` (`--all-interactive`) puts the mode on `HeadOutlet` and `Routes` in `App.razor`, so every page inherits it.

| Template command | Scope | `.Client` project | `Routes` in `App.razor` | Sample page |
| --- | --- | --- | --- | --- |
| `-int None` | Static SSR only | No | `<Routes />` | none; no interactive endpoints |
| `-int Server` | **Per-page** | No | `<Routes />` (static) | `Counter.razor` has `@rendermode InteractiveServer` |
| `-int Server -ai` | **Global** | No | `<Routes @rendermode="InteractiveServer" />` | pages inherit; no per-page directive |
| `-int WebAssembly` | **Per-page** | Yes | `<Routes />` (static) | `Counter.razor` has `@rendermode InteractiveWebAssembly` |
| `-int WebAssembly -ai` | **Global** | Yes | `<Routes @rendermode="InteractiveWebAssembly" />` | pages inherit |
| `-int Auto` | **Per-page** | Yes | `<Routes />` (static) | `Counter.razor` has `@rendermode InteractiveAuto` |
| `-int Auto -ai` | **Global** | Yes | `<Routes @rendermode="InteractiveAuto" />` | pages inherit |
| Legacy `blazorserver` (`_Host.cshtml`) | Always interactive | No | n/a | no render modes |
| Standalone `blazorwasm` | Always interactive | n/a | n/a | no render modes |

**Do not assume `-int Server` is global.** Only `-ai` is. With the default per-page template, any page that is not marked with a `@rendermode` is static SSR.

## Rule for placing Toolkit components

1. Find the scope: read `Components/App.razor`. If `Routes` (and `HeadOutlet`) carry `@rendermode="..."`, the app is **global** and every page inherits that mode. If `<Routes />` has no directive, the app is **per-page**.
2. **Global**: place Toolkit components anywhere. Do not add a different `@rendermode` to a page; a child cannot switch to a different interactive mode.
3. **Per-page**: put every Toolkit component that uses events, bindings, dialogs, calendar navigation, or button clicks on a page or component that has its own `@rendermode`, or inside a component that is rendered from such a page. Pages without a directive are static SSR and must only host display-only Toolkit markup.
4. Choose the mode to match the template: `InteractiveServer` for `-int Server`, `InteractiveWebAssembly` for `-int WebAssembly`, `InteractiveAuto` for `-int Auto`. Use a mode the app already supports; never add endpoints for Toolkit.

## Static SSR

**When to use**: Read-only content with no user interaction.

**Toolkit limitation**: Static SSR is not sufficient for interactive Toolkit components. Event handlers and state changes will not work. Toolkit services are still required: a static page that renders `SfButton` returns HTTP 500 without `AddSyncfusionBlazorToolkit()`.

**Example (display-only)**:
```razor
@page "/status"
@using Syncfusion.Blazor.Toolkit.Buttons

<SfButton Disabled="true">Read-only button</SfButton>
```

## Interactive Server

**Per-page (`-int Server`)**: add `@rendermode InteractiveServer` to each page that hosts interactive Toolkit components.

```razor
@page "/counter"
@rendermode InteractiveServer
@using Syncfusion.Blazor.Toolkit.Buttons

<p>Count: @count</p>
<SfButton OnClick="IncrementCount">Increment</SfButton>

@code {
    private int count = 0;
    private void IncrementCount() => count++;
}
```

**Global (`-int Server -ai`)**: `Routes` already carries `@rendermode="InteractiveServer"`. Write the same page **without** the `@rendermode` line.

**`Program.cs`**: leave the template's `AddInteractiveServerComponents()` and `AddInteractiveServerRenderMode()` as generated. Add only `using Syncfusion.Blazor.Toolkit;` and `builder.Services.AddSyncfusionBlazorToolkit();` before `builder.Build()`. Do not add WebAssembly services to this template.

## Interactive WebAssembly

Interactive pages live in the `.Client` project. Default prerendering still runs the component on the server first, so the server also needs Toolkit services.

**Per-page (`-int WebAssembly`)**: add `@rendermode InteractiveWebAssembly` to the page, and keep that page in the `.Client` project. Verified: an `InteractiveWebAssembly` page placed in the server project returns HTTP 200 and shows the button from the prerender, then fails in the browser with `Root component type '...' could not be found in the assembly '<ServerApp>'`.

```razor
@page "/counter"
@rendermode InteractiveWebAssembly
@using Syncfusion.Blazor.Toolkit.Buttons

<p>Count: @count</p>
<SfButton OnClick="IncrementCount">Increment</SfButton>

@code {
    private int count = 0;
    private void IncrementCount() => count++;
}
```

**Global (`-int WebAssembly -ai`)**: `Routes` already carries `@rendermode="InteractiveWebAssembly"`. Omit the page directive.

**`Program.cs`** (server and `.Client`): the template registers WebAssembly endpoints only. Keep them and `AddAdditionalAssemblies(...)` on `MapRazorComponents<App>()`. Add `using Syncfusion.Blazor.Toolkit;` and `builder.Services.AddSyncfusionBlazorToolkit();` to **both** the server and `.Client/Program.cs`. Do not add `RootComponents.Add<App>("#app")` to `.Client`. See [Split Blazor Web App registration](./split-webapp-registration.md).

**Standalone WebAssembly** is a different app (no Web App, no `.Client`): it is always interactive, has no `@rendermode`, and its `Program.cs` already contains `RootComponents.Add<App>("#app")`. Add only the Toolkit `using` and the registration.

## Interactive Auto

First visit runs on the server; later visits use the cached WebAssembly bundle. A running component does not switch runtimes. As with WebAssembly, interactive Auto pages and components must live in the `.Client` project, because the same code has to run in both places. Verified: an `@rendermode InteractiveAuto` page placed in the server project returns HTTP 200 and shows the button from the prerender, then fails in the browser with `Root component type '...' could not be found in the assembly '<ServerApp>'`, so it never becomes interactive. See https://learn.microsoft.com/aspnet/core/blazor/components/render-modes

**Per-page (`-int Auto`)**: add `@rendermode InteractiveAuto` to the page, and keep that page in the `.Client` project (the template's sample `Counter.razor` is in `<App>.Client/Pages`).

```razor
@page "/counter"
@rendermode InteractiveAuto
@using Syncfusion.Blazor.Toolkit.Buttons

<p>Count: @count</p>
<SfButton OnClick="IncrementCount">Increment</SfButton>

@code {
    private int count = 0;
    private void IncrementCount() => count++;
}
```

**Global (`-int Auto -ai`)**: `Routes` already carries `@rendermode="InteractiveAuto"`. Omit the page directive.

**`Program.cs`** (server and `.Client`): the template registers both Interactive Server and Interactive WebAssembly, plus `AddAdditionalAssemblies(...)`. Keep all of it. Add the Toolkit `using` and `AddSyncfusionBlazorToolkit()` to **both** the server and `.Client/Program.cs`. Do not add `RootComponents.Add<App>("#app")` to `.Client`.

## Decision Tree

1. **Does the component handle clicks, bindings, dialogs, calendar navigation, or any client interaction?**
   - **No** → Static rendering is enough (services are still required). Stop.
   - **Yes** → Continue.
2. **Which app is this?**
   - **Legacy `_Host.cshtml` Server or standalone WebAssembly** → Already interactive. Do not add `@rendermode`.
   - **Blazor Web App** → Continue.
3. **Is `Routes` in `App.razor` already carrying `@rendermode`?**
   - **Yes (global, `-ai`)** → Inherit it. Do not add a different mode.
   - **No (per-page)** → Add exactly one directive on the page that hosts the component, matching the template: `InteractiveServer`, `InteractiveWebAssembly`, or `InteractiveAuto`.

An interactive Toolkit component must never be left on a page with no `@rendermode` in a per-page app.

## Common Mistakes

1. **Assuming `-int Server` is global**: it is per-page. Only `-ai` is global. A page without `@rendermode` is static SSR in a per-page app.
2. **Leaving an interactive Toolkit component on a static page**: clicks, bindings, and dialogs do nothing. Add the template's `@rendermode` to the page.
3. **Adding `@rendermode` under an inherited mode**: if `Routes` already has one (global), a child that names a different mode fails. Match the parent or move the component.
4. **Treating standalone WebAssembly or legacy Server as needing `@rendermode`**: they have no render modes.
5. **Adding WebAssembly endpoints to a server-only project**: Toolkit does not need them.
6. **Mixing the WebAssembly and Auto templates**: `-int WebAssembly` registers WebAssembly endpoints only; `-int Auto` registers both Server and WebAssembly. Keep the shape the template generated.
7. **Editing endpoints to "fit" Toolkit**: Toolkit needs only `AddSyncfusionBlazorToolkit()`. Do not add or remove `AddInteractive*` or `AddAdditionalAssemblies(...)` calls.
8. **Describing Auto as a live runtime switch**: a running component does not migrate.
9. **Assuming a missing directive means the component is interactive or static**: read `Routes` in `App.razor` first.
