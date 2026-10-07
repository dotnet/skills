# Render Modes Reference

Blazor supports multiple render modes. Toolkit components need an interactive mode whenever they handle clicks, binding, or other live updates.

> **Framework support.** The Toolkit targets `net8.0`, `net9.0`, and `net10.0`. If `<TargetFramework>` is older, recommend upgrading first: restore succeeds on `net7.0` and earlier but supplies no assemblies, so the build fails with `CS0246`.

> **Namespaces.** `SfButton` lives in `Syncfusion.Blazor.Toolkit.Buttons`, so each sample below includes `@using Syncfusion.Blazor.Toolkit.Buttons`. If it is already in `_Imports.razor`, the per-page line can be dropped.

Toolkit itself only needs `AddSyncfusionBlazorToolkit()`. Keep every `AddInteractive*` and `AddAdditionalAssemblies(...)` call exactly as the project template generated it; do not add or remove them for Toolkit.

## Static SSR (Server-Side Rendering)

**When to use**: Read-only content where no user interaction or dynamic updates are needed.

**Characteristic**: Components are rendered on the server and sent as HTML; no .NET code runs on the client.

**Toolkit limitation**: Static SSR is not sufficient for interactive Toolkit components (buttons, forms, inputs, etc.). Event handlers and state changes will not work. Toolkit services are still required: a static page that renders `SfButton` returns HTTP 500 without `AddSyncfusionBlazorToolkit()`.

**Example (read-only only)**:
```razor
@page "/status"
@using Syncfusion.Blazor.Toolkit.Buttons

<SfButton Disabled="true">Read-only button</SfButton>
```

## Interactive Server Rendering

**When to use**: Interactive components on a Blazor Web App configured for Interactive Server, or a page that opts into `@rendermode InteractiveServer`.

**Characteristic**: User interactions are sent to the server, processed, and updates stream back to the client.

**Toolkit requirement**: Fully supported. Register Toolkit in the server `Program.cs`.

**Example** (only when the page does not already inherit an interactive mode):
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

**Server `Program.cs`** (`dotnet new blazor -int Server`): leave the template's `AddInteractiveServerComponents()` and `AddInteractiveServerRenderMode()` as generated, and add only `using Syncfusion.Blazor.Toolkit;` plus `builder.Services.AddSyncfusionBlazorToolkit();` before `builder.Build()`. Do not add WebAssembly services to this template.

## Interactive WebAssembly Rendering

**When to use**: A Blazor Web App page or component that should run in the browser after download (`dotnet new blazor -int WebAssembly`, or a component in the `.Client` project).

**Characteristic**: .NET runs in the browser; no server round-trip for user interactions after load. Default prerendering still executes the component on the server first, so the server also needs Toolkit services.

**Do not use `@rendermode` in a standalone Blazor WebAssembly app.** That app is already interactive. The directive has no effect there and can look like a fix for a different problem.

**Blazor Web App example** (only when the page does not already inherit an interactive mode):
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

**Server and `.Client` `Program.cs`** (`dotnet new blazor -int WebAssembly`): the template registers WebAssembly endpoints only. Keep them, and keep `AddAdditionalAssemblies(...)` on `MapRazorComponents<App>()`. Add `using Syncfusion.Blazor.Toolkit;` and `builder.Services.AddSyncfusionBlazorToolkit();` to **both** the server and `.Client/Program.cs` (prerendering runs the component on the server). Do not add `RootComponents.Add<App>("#app")` to `.Client`. See [Split Blazor Web App registration](./split-webapp-registration.md).

**Standalone WebAssembly `Program.cs`** (no Web App, no `.Client` project): the template already has `RootComponents.Add<App>("#app")` and `RootComponents.Add<HeadOutlet>("head::after")`. Add only `using Syncfusion.Blazor.Toolkit;` and `builder.Services.AddSyncfusionBlazorToolkit();` before `Build()`.

## Interactive Auto Rendering

**When to use**: A Blazor Web App that should use server interactivity on the first visit, then WebAssembly on later visits once the runtime bundle is downloaded and cached.

**Characteristic**: Auto chooses the runtime per visit. A component that is already running does not switch from server to WebAssembly mid-circuit. See https://learn.microsoft.com/aspnet/core/blazor/components/render-modes

**Toolkit requirement**: Fully supported. Register Toolkit in both the server and `.Client` projects, because the first visit renders on the server and later visits render in WebAssembly.

**Example** (page-level `@rendermode InteractiveAuto`):
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

**Server and `.Client` `Program.cs`** (`dotnet new blazor -int Auto`): the template registers both Interactive Server and Interactive WebAssembly, and `AddAdditionalAssemblies(...)` on `MapRazorComponents<App>()`. Keep all of it. Add `using Syncfusion.Blazor.Toolkit;` and `builder.Services.AddSyncfusionBlazorToolkit();` to **both** the server and `.Client/Program.cs`. Do not add `RootComponents.Add<App>("#app")` to `.Client`.

## Decision Tree

1. **Does the component need to respond to user clicks or changes?**
   - **No** → Static rendering is enough. Stop.
   - **Yes** → Go to question 2.

2. **Which app is this?**
   - **Legacy Blazor Server or standalone WebAssembly** → Already interactive. Do not add `@rendermode`.
   - **Blazor Web App** → Go to question 3.

3. **Does `Routes` in `App.razor` already apply an interactive mode?**
   - **Yes** → Inherit it. A child cannot switch to a different interactive mode.
   - **No** → Apply one mode on the page or component:
     - Server processing → `@rendermode InteractiveServer`
     - Browser processing → `@rendermode InteractiveWebAssembly`
     - Server on the first visit, WebAssembly on later cached visits → `@rendermode InteractiveAuto`

## Common Mistakes

1. **Adding `@rendermode` under an inherited mode**: If `Routes` already has an interactive mode, a child that names a different one fails. Match the parent or move the component.
2. **Treating standalone WebAssembly as static SSR**: It has no static SSR and no render-mode directive.
3. **Adding WebAssembly endpoints to a server-only project**: That project may not reference the WebAssembly server package.
4. **Mixing the WebAssembly and Auto templates**: `-int WebAssembly` registers WebAssembly endpoints only; `-int Auto` registers both Server and WebAssembly. Keep the shape the template generated.
5. **Editing endpoints to "fit" Toolkit**: Toolkit needs only `AddSyncfusionBlazorToolkit()`. Do not add or remove `AddInteractive*` or `AddAdditionalAssemblies(...)` calls for it.
6. **Describing Auto as a live runtime switch**: The first visit uses the server; later visits use the cached WebAssembly bundle. A running component does not migrate.
7. **Assuming a missing directive means the component is static**: Check the inherited mode first.

