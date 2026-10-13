# Render Modes Reference

Toolkit components need an **interactive boundary** whenever they handle clicks, bindings, dialogs, calendar navigation, or any other client interaction. A Toolkit component with no interactive ancestor is display-only.

> **Framework support.** `Syncfusion.Blazor.Toolkit` 2.0.0 targets `net8.0`, `net9.0`, and `net10.0`. Always check the project's `<TargetFramework>` before installation. On `net6.0`, `net7.0`, or older frameworks, NuGet restore fails with an incompatibility such as `NU1202` because the package has no compatible assets for those TFMs. Do not expect a later `CS0246` as the primary signal—fix the framework mismatch first. Do not proceed with Toolkit installation on unsupported frameworks. Recommend upgrading the application to .NET 8 or later first, then continue with the installation steps.

> **Namespaces (2.0).** All public component types—including `SfButton`—live in the single root namespace `Syncfusion.Blazor.Toolkit`. Domain-specific namespaces from 1.x were removed. Each sample below includes `@using Syncfusion.Blazor.Toolkit`. If it is already in `_Imports.razor`, the per-page line can be dropped.

Toolkit itself only needs `AddSyncfusionBlazorToolkit()`. Keep every `AddInteractive*` and `AddAdditionalAssemblies(...)` call exactly as the project template generated it; do not add or remove them for Toolkit.

## Template matrix (verified with `dotnet new blazor`)

`dotnet new blazor -int <mode>` makes the **server project** interactive-capable but leaves pages **static** until a page (or a child component) opts in. Adding `-ai` (`--all-interactive`) puts the mode on `HeadOutlet` and `Routes` in `App.razor`, so every page inherits it.

| Template command | Scope | `.Client` project | `Routes` in `App.razor` | Sample page |
| --- | --- | --- | --- | --- |
| `-int None` | Static SSR only | No | `<Routes />` | none; no interactive endpoints |
| `-int Server` | **Per-page** | No | `<Routes />` (static) | `Counter.razor` has `@rendermode InteractiveServer` |
| `-int Server -ai` | **Global** | No | `<Routes @rendermode="InteractiveServer" />` | pages inherit; no per-page directive |
| `-int WebAssembly` | **Per-page** | Yes | `<Routes />` (static) | `Counter.razor` has `@rendermode InteractiveWebAssembly` |
| `-int WebAssembly -ai` | **Global** | Yes | `<Routes @rendermode="InteractiveWebAssembly" />` | pages inherit |
| `-int Auto` | **Per-page** | Yes | `<Routes />` (static) | `Counter.razor` has `@rendermode InteractiveAuto` |
| `-int Auto -ai` | **Global** | Yes | `<Routes @rendermode="InteractiveAuto" />` | pages inherit |

## Static SSR only (`-int None` or a page with no interactive boundary)

**Toolkit limitation**: Static SSR is not sufficient for interactive Toolkit components. Event handlers and state changes will not work. Toolkit services are still required: a static page that renders `SfButton` returns HTTP 500 without `AddSyncfusionBlazorToolkit()`.

**Example (display-only)**:
```razor
@page "/status"
@using Syncfusion.Blazor.Toolkit

<SfButton Disabled="true">Read-only button</SfButton>
```

## Interactive Server

**Per-page (`-int Server`)**: ensure an interactive boundary. Either:

- add `@rendermode InteractiveServer` on the **page**, or
- keep the page static and wrap the Toolkit control in a **child component** that declares `@rendermode InteractiveServer`.

```razor
@page "/counter"
@rendermode InteractiveServer
@using Syncfusion.Blazor.Toolkit

<p>Count: @count</p>
<SfButton OnClick="IncrementCount">Increment</SfButton>

@code {
    private int count = 0;
    private void IncrementCount() => count++;
}
```

**Global (`-int Server -ai`)**: `Routes` already carries `@rendermode="InteractiveServer"`. Write the same page **without** a page-level `@rendermode` line (and do not put a different mode on a child).

**`Program.cs`**: leave the template's `AddInteractiveServerComponents()` on `AddRazorComponents()` and `AddInteractiveServerRenderMode()` on `MapRazorComponents<App>()` as generated. Add only `using Syncfusion.Blazor.Toolkit;` and `builder.Services.AddSyncfusionBlazorToolkit();` before `builder.Build()`. Do **not** add WebAssembly services to this template.

## Interactive WebAssembly (Blazor Web App)

Interactive pages live in the `.Client` project. Default prerendering still runs the component on the server first, so the server also needs Toolkit services.

**Per-page (`-int WebAssembly`)**: interactive boundary on the page **or** a wrapper component, both in `.Client`, with `@rendermode InteractiveWebAssembly`. Verified: an `InteractiveWebAssembly` page placed in the server project returns HTTP 200 and shows the button from the prerender, then fails in the browser with `Root component type '...' could not be found in the assembly '<ServerApp>'`.

```razor
@page "/counter"
@rendermode InteractiveWebAssembly
@using Syncfusion.Blazor.Toolkit

<p>Count: @count</p>
<SfButton OnClick="IncrementCount">Increment</SfButton>

@code {
    private int count = 0;
    private void IncrementCount() => count++;
}
```

**Global (`-int WebAssembly -ai`)**: `Routes` already carries `@rendermode="InteractiveWebAssembly"`. Omit the page directive. Interactive pages and Toolkit components still live in the **`.Client` project** (the generated template puts `Routes` and pages there); do not put interactive Toolkit pages under the server project.

**`Program.cs`** (server and `.Client`): the template registers WebAssembly endpoints only. Keep them and `AddAdditionalAssemblies(...)` on `MapRazorComponents<App>()`. Add `using Syncfusion.Blazor.Toolkit;` and `builder.Services.AddSyncfusionBlazorToolkit();` to **both** the server and `.Client/Program.cs`. Do not add `RootComponents.Add<App>("#app")` to `.Client`. See [Split Blazor Web App registration](./split-webapp-registration.md).

### Standalone WebAssembly (not a Web App)

Standalone Blazor WebAssembly is a different app (no Web App host, no `.Client` split): it is **already interactive**, has **no** `@rendermode`, and its `Program.cs` already contains `RootComponents.Add<App>("#app")`. Do **not** add `@rendermode InteractiveWebAssembly`. Add only the Toolkit `using` and `AddSyncfusionBlazorToolkit()`.

## Interactive Auto

First visit uses server interactivity; later visits use the cached WebAssembly bundle once it has downloaded. A **running** component does **not** switch runtimes mid-session. As with WebAssembly, interactive Auto pages and components must live in the `.Client` project. Verified: an `@rendermode InteractiveAuto` page in the server project prerenders then fails in the browser with `Root component type '...' could not be found in the assembly '<ServerApp>'`. See https://learn.microsoft.com/aspnet/core/blazor/components/render-modes

**Per-page / global**: same boundary rules as WebAssembly, with `@rendermode InteractiveAuto` (or inherited from `Routes` when using `-ai`). Interactive pages and Toolkit components must live in **`.Client`** for both per-page and global Auto.

**`Program.cs`** (server and `.Client`): the template registers **both** Interactive Server and Interactive WebAssembly, plus `AddAdditionalAssemblies(...)`. Keep all of it. Add the Toolkit `using` and `AddSyncfusionBlazorToolkit()` to **both** hosts. Do not add `RootComponents.Add<App>("#app")` to `.Client`.

## Decision Tree

1. **Does the component handle clicks, bindings, dialogs, calendar navigation, or any client interaction?**
   - **No** → Static rendering is enough (services are still required). Stop.
   - **Yes** → Continue.
2. **Which app is this?**
   - **Legacy `_Host.cshtml` Server or standalone WebAssembly** → Already interactive. Do **not** add `@rendermode`.
   - **Blazor Web App** → Continue.
3. **Is `Routes` in `App.razor` already carrying `@rendermode`?**
   - **Yes (global, `-ai`)** → Inherit it. Do not add a different mode on a child.
   - **No (per-page)** → The Toolkit control (and its handlers) must sit inside an **interactive boundary**. Either:
     - put `@rendermode InteractiveServer` / `InteractiveWebAssembly` / `InteractiveAuto` (matching the template) on the **page**, or
     - keep the page static SSR and wrap the Toolkit control in a **child component** that declares the same `@rendermode` (Blazor allows interactive children on a static page).

Do **not** leave an interactive Toolkit control with no interactive ancestor. The page itself does not have to be interactive.

## Common Mistakes

1. **Assuming `-int Server` is global**: it is per-page. Only `-ai` is global. A page without an interactive boundary is static SSR in a per-page app.
2. **Leaving an interactive Toolkit component outside any interactive boundary**: clicks, bindings, and dialogs do nothing. Use a page `@rendermode` **or** an interactive wrapper so the page can stay static SSR.
3. **Adding `@rendermode` under an inherited mode**: if `Routes` already has one (global), a child that names a different mode fails. Match the parent or move the component.
4. **Treating standalone WebAssembly or legacy Server as needing `@rendermode`**: they have no render modes.
5. **Adding WebAssembly endpoints to a server-only project**: Toolkit does not need them; keep the template shape.
6. **Mixing the WebAssembly and Auto templates**: `-int WebAssembly` registers WebAssembly only; `-int Auto` registers both. Keep what the template generated.
7. **Editing endpoints to "fit" Toolkit**: Toolkit needs only `AddSyncfusionBlazorToolkit()`. Do not add or remove `AddInteractive*` or `AddAdditionalAssemblies(...)`.
8. **Describing Auto as a live runtime switch**: a running component does not migrate mid-session.
9. **Assuming a missing directive means the component is interactive**: read `Routes` in `App.razor` first, then the page or wrapper.
