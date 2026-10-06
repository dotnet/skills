# Render Modes Reference

Blazor supports multiple render modes. Toolkit components need an interactive mode whenever they handle clicks, binding, or other live updates.

## Static SSR (Server-Side Rendering)

**When to use**: Read-only content where no user interaction or dynamic updates are needed.

**Characteristic**: Components are rendered on the server and sent as HTML; no .NET code runs on the client.

**Toolkit limitation**: **Static SSR is not sufficient for interactive Toolkit components** (buttons, forms, dropdowns, etc.). Event handlers and state changes will not work.

**Example (read-only only)**:
```razor
@page "/counter"

<p>This counter is stuck at: @count</p>
<SfButton Disabled="true">Click doesn't work in SSR</SfButton>

@code {
    private int count = 0;
    // @onclick handlers don't fire in static SSR
}
```

## Interactive Server Rendering

**When to use**: Interactive components on a Blazor Web App configured for Interactive Server, or a page that opts into `@rendermode InteractiveServer`.

**Characteristic**: User interactions are sent to the server, processed, and updates stream back to the client.

**Toolkit requirement**: Fully supported. Register Toolkit in the server `Program.cs`.

**Example** (only when the page does not already inherit an interactive mode):
```razor
@page "/counter"
@rendermode InteractiveServer

<p>Count: @count</p>
<SfButton @onclick="IncrementCount">Increment</SfButton>

@code {
    private int count = 0;
    private void IncrementCount() => count++;
}
```

**Server-only template setup**. Do not add WebAssembly services here; a server-only project may not reference `Microsoft.AspNetCore.Components.WebAssembly.Server`.

```csharp
using Syncfusion.Blazor.Toolkit;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSyncfusionBlazorToolkit();

var app = builder.Build();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
```

## Interactive WebAssembly Rendering

**When to use**: A Blazor Web App page or component that should run in the browser after download (`dotnet new blazor -int WebAssembly`, or a component in the `.Client` project).

**Characteristic**: .NET runs in the browser; no server round-trip for user interactions after load. Default prerendering still executes the component on the server first, so the server also needs Toolkit services.

**Do not use `@rendermode` in a standalone Blazor WebAssembly app.** That app is already interactive. The directive has no effect there and can look like a fix for a different problem.

**Blazor Web App example** (only when the page does not already inherit an interactive mode):
```razor
@page "/counter"
@rendermode InteractiveWebAssembly

<p>Count: @count</p>
<SfButton @onclick="IncrementCount">Increment</SfButton>

@code {
    private int count = 0;
    private void IncrementCount() => count++;
}
```

**`.Client/Program.cs`** (not a standalone app; no root component):
```csharp
using Syncfusion.Blazor.Toolkit;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddSyncfusionBlazorToolkit();

await builder.Build().RunAsync();
```

**Standalone WebAssembly `Program.cs`**:
```csharp
using Syncfusion.Blazor.Toolkit;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddSyncfusionBlazorToolkit();

await builder.Build().RunAsync();
```

## Interactive Auto Rendering

**When to use**: A Blazor Web App that should use server interactivity on the first visit, then WebAssembly on later visits once the runtime bundle is downloaded and cached.

**Characteristic**: Auto chooses the runtime per visit. A component that is already running does not switch from server to WebAssembly mid-circuit. See the render-mode documentation: https://learn.microsoft.com/aspnet/core/blazor/components/render-modes

**Toolkit requirement**: Fully supported. Register Toolkit in both the server and `.Client` projects, because the first visit renders on the server and later visits render in WebAssembly.

**Example**:
```razor
@page "/counter"
@rendermode InteractiveAuto

<p>Count: @count</p>
<SfButton @onclick="IncrementCount">Increment</SfButton>

@code {
    private int count = 0;
    private void IncrementCount() => count++;
}
```

**Server setup** (keep both interactive endpoints; this is the combined hosting case, not the server-only case):
```csharp
using Syncfusion.Blazor.Toolkit;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddSyncfusionBlazorToolkit();

var app = builder.Build();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode();
```

Also call `AddSyncfusionBlazorToolkit()` in `.Client/Program.cs`. Do not add `RootComponents.Add<App>("#app")` there.

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
4. **Describing Auto as a live runtime switch**: The first visit uses the server; later visits use the cached WebAssembly bundle. A running component does not migrate.
5. **Assuming a missing directive means the component is static**: Check the inherited mode first.
