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

**When to use**: Interactive components hosted on a Blazor Server app or on the Server side of a Blazor Web App.

**Characteristic**: User interactions are sent to the server, processed, and updates stream back to the client in real time.

**Toolkit requirement**: Fully supported; all event handlers and state changes work.

**Example**:
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

**Current template setup**:
```csharp
// Program.cs
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

## Interactive WebAssembly Rendering

**When to use**: Interactive components running as WebAssembly on the client (standalone Blazor WebAssembly or the Client project in a Blazor Web App).

**Characteristic**: .NET runs in the browser; no server round-trip for user interactions after load.

**Toolkit requirement**: Fully supported; all event handlers and state changes work.

**Example**:
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

**Setup**:
```csharp
// Program.cs (standalone WASM or .Client project)
var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");

builder.Services.AddSyncfusionBlazorToolkit();

await builder.Build().RunAsync();
```

## Interactive Auto Rendering

**When to use**: Blazor Web App projects that should start on the server and then continue running in the browser when available.

**Characteristic**: The app can begin with server interactivity and transition to client-side WebAssembly for later interactions.

**Toolkit requirement**: Fully supported; all event handlers and state changes work.

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

**Setup**:
```csharp
// Program.cs
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

## Decision Tree

1. **Does the component need to respond to user clicks or changes?**
   - **No** → Static SSR is fine (no render mode needed)
   - **Yes** → Go to question 2

2. **Should processing happen on the server, in the browser, or both?**
   - **Server** → Use `@rendermode InteractiveServer`
   - **Browser (WASM)** → Use `@rendermode InteractiveWebAssembly`
   - **Start on server, continue in browser** → Use `@rendermode InteractiveAuto`

3. **Toolkit component won't respond?**
   - **Symptom**: Click or input events don't work
   - **Solution**: Ensure the component or page has an interactive render mode applied

## Common Mistakes

1. **Forgetting `@rendermode`**: Interactive components in a static page won't respond to clicks.
2. **Using static SSR for interactive components**: Toolkit components require interactive mode for event handling.
3. **Mismatched render mode**: Trying to use `InteractiveServer` in a WebAssembly-only app won't work.
4. **Ignoring `InteractiveAuto`**: Auto is a first-class render mode for current Blazor Web App templates.
5. **Assuming automatic**: Render mode doesn't "just work"; it must be explicitly declared.
