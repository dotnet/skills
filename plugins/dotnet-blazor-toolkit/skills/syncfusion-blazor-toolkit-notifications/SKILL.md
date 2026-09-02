---
license: MIT
name: syncfusion-blazor-toolkit-notifications
description: >
  Implement Syncfusion Blazor Toolkit loading and activity indicators —
  SfSpinner.
  USE FOR: form submission and async-operation feedback, content loading
  decorations, overlay compositions with z-index stacking, accessibility-
  compliant loading announcements (aria-live), programmatic visibility
  toggles via @bind-Visible or VisibleChanged, and cancelable open/close
  events.
  REQUIRES interactive render mode (Server, WebAssembly, or Auto @
  .NET 8+). Overlay patterns require JS interop for body scroll lock.
  DO NOT USE FOR: full-page skeleton loaders (use SfSkeleton — not in
  this skill), action-bearing toast notifications (use SfToast — not in
  this skill), or progress bars with explicit percent (use
  SfProgressBar — not in this skill).
compatibility: ".NET 8+, render-modes: Server, WebAssembly, Auto"
metadata:
  author: "Syncfusion Inc"
  version: "1.0.0"
---

# Syncfusion Blazor Toolkit: Notifications & Loading Indicators

> ⚙️ **Render mode:** `SfSpinner` requires an **interactive** render mode (Server, WebAssembly, or Auto @ .NET 8+). In **Static SSR**, prefer the built-in `[StreamRendering]` attribute on the page (no `SfSpinner` needed) — see `references/spinner-overlay.md` for the static-mode escape hatch. Read `AGENTS.md` before picking a strategy.

The Notifications components provide visual feedback during asynchronous operations and content loading scenarios. This skill guides you through implementing the **Spinner** component for indicating background processing.

## Core Rules

1. **Use `@bind-Visible` for state OR use `Visible` + `VisibleChanged`** —
   never both. Combine = re-render storms and "stuck open" spinners.
2. **`OnOpen` and `OnClose` `args.Cancel` aborts the action**; they run *before*
   the visible change fires. Don't expect the spinner to hide synchronously
   after `OnClose` returns.
3. **`Label` is announced via aria-live automatically.** Either:
   - Set `Label="Loading…"` and don't put text inside `<Template>`, OR
   - Use `<Template>` and add `aria-hidden="true"` on the visual nodes.
   Never both.
4. **Spinner renders inline.** For overlay positioning, supply `CssClass`
   with `position: absolute/fixed` rules.
5. **body scroll lock requires JS interop (`IJSRuntime`).** It's not
   free — wire `IAsyncDisposable.DisposeAsync()` to release the lock.
6. **`VisibleChanged` is `EventCallback<bool>`** — fire-and-forget handlers
   will swallow exceptions. Use `async Task` handlers.
7. **Render the overlay via `InvokeAsync(StateHasChanged)` BEFORE the
   long-running `await`**, otherwise the overlay renders after the await
   and serves no purpose.
8. **Don't inset `SfSpinner` inside an interactive button** — that defeats
   the click target. Use a custom button replacement or `aria-hidden`.

## Don'ts

| Anti-pattern | Symptom | Fix |
|---|---|---|
| `Visible="@_busy" @bind-Visible VisibleChanged="@(v => _busy = v)"` | Re-render storm; spinner "stuck open" or "stuck closed" | Pick exactly one: `@bind-Visible="@_busy"` |
| Using `SfSpinner` for confirmation ("are you sure?") | User double-clicks the button before the spinner fades out; double-commit | Use `Syncfusion.Blazor.Toolkit.Popups.SfDialog` with `IsModal="true"` |
| `position: fixed` overlay inside a Static SSR page | `JS` not available pre-interactivity; overlay renders without scroll lock | Either upgrade to interactive render mode, or rely on element-level overlay (don't lock body scroll) |
| `<SfSpinner>` nested inside `<SfButton>` | Pointer events pass through; user clicks the button while spinner is "showing" | Use `Disabled="@_busy"` on the button, and place `<SfSpinner>` *adjacent to* (not inside) the button |
| No `role="alert"` / `aria-busy="true"` on the parent overlay element | Screen readers don't know the page is busy; user confused | Wrap with `<div role="alert" aria-busy="true">` while the spinner is up |
| `Label=""` (or omitted) | WCAG 2.1 violation; spinner announces nothing visually either | Always set `Label="Loading…"` with at least 3 chars / aria-friendly text |
| Disabling the parent button to "pause interaction" | Confusing — button looks inert but no feedback | Use a visible spinner overlay with explicit `pointer-events: none`, or render the button as "Saving…" text while disabled |
| `OnClose` setting `args.Cancel = true` and then mutating `Visible` directly | `OnClose` runs sync; race condition opens the spinner | `args.Cancel = true` only; let the binding decide visibility |
| Two `SfSpinner` instances bound to the same `bool` | Two overlay layers stacked; performance + z-index fights | One spinner; resize via `CssClass` |
| `OnOpen = async void` | Exceptions silently escape | `OnOpen = async Task …` or use `ValueTask` |

## Anti-Pattern Workflows

### Workflow 1 — Agent wires three visibility bindings

**Bad:**
```razor
<SfSpinner @ref="spinner"
           Visible="@_busy"
           @bind-Visible="@_busy"
           VisibleChanged="@(v => _busy = v)"
           Label="Saving…" />
<!-- infinite re-renders -->
```

**Fix:** pick exactly one. `@bind-Visible` is enough:

```razor
<SfSpinner @bind-Visible="@_busy" Label="Saving…" />
```

### Workflow 2 — Agent's overlay renders AFTER the await

**Bad:**
```razor
private async Task Submit()
{
    _busy = true;
    await SaveAsync();  // overlay exists in markup but block is synchronous on first render
    _busy = false;
}
```

**Why it fails:** even though `_busy = true` triggers a render, the
async method starts the `SaveAsync` call immediately and **re-renders again**
only when awaited. Browser paints on the second invocation, *after* completion.

**Fix:** force a render first, then await:

```razor
private async Task Submit()
{
    _busy = true;
    await InvokeAsync(StateHasChanged);   // paint overlay NOW
    try   { await SaveAsync(); }
    finally { _busy = false; }
}
```

### Workflow 3 — Agent wants confirmation "do you really want to save?" with a spinner

**Anti-pattern:** raise the spinner for 3s before commit, hoping the user
will click away.

**Fix:** use a modal dialog. First load the popups **router** —
[syncfusion-blazor-toolkit-popups](../syncfusion-blazor-toolkit-popups/SKILL.md) —
to check the dialog-vs-tooltip decision matrix, then dive into the
[syncfusion-blazor-toolkit-dialog sub-skill](../syncfusion-blazor-toolkit-popups/dialog/SKILL.md).

```razor
<SfDialog @bind-Visible="_confirm" IsModal="true" Header="Confirm Save">
    <DialogButtons>
        <DialogButton Content="Yes" OnClick="@ConfirmYes" />
        <DialogButton Content="No"  />
    </DialogButtons>
</SfDialog>

@code {
    private async Task ConfirmYes() { _confirm = false; _busy = true; await InvokeAsync(StateHasChanged); await SaveAsync(); _busy = false; }
}
```

Spinner for *post-confirmation* (during actual save); dialog gates *user input*.

### Workflow 4 — Agent's overlay uses `body { overflow: hidden }` but never clears

**Bad:**
```razor
@if (_busy) <SfSpinner @bind-Visible="@_busy" />
@code { protected override void OnAfterRender(bool _) { if (_busy) JS.InvokeVoidAsync("lockBody"); } }
<!-- user navigates away; lock persists -->
```

**Fix:** centralize lock/unlock in `IAsyncDisposable`:

```razor
@implements IAsyncDisposable
@code {
    protected override void OnAfterRender(bool _) { if (_busy) JS.InvokeVoidAsync("lockBody"); }
    public async ValueTask DisposeAsync() { try { await JS.InvokeVoidAsync("unlockBody"); } catch { } }
}
```

### Workflow 5 — Agent nests `SfSpinner` inside `SfButton` for a submit button

**Anti-pattern:**
```razor
<SfButton Disabled="@_busy" OnClick="SubmitAsync">
    @if (_busy) <SfSpinner Visible="true" Size="14" />
    Submit
</SfButton>
```

**Why it fails:** in Static SSR, the spinner DOM never mounts; pointer events
on the button area conflict with the inner spinner's wrapper.

**Fix #1 — label swap (simplest):**
```razor
<SfButton Disabled="@_busy" OnClick="SubmitAsync">
    @(_busy ? "Saving…" : "Submit")
</SfButton>
```

**Fix #2 — external spinner overlay around the whole form:**
see [references/spinner-overlay.md §2.5](references/spinner-overlay.md#25-form-submission-spinner-editform--sfspinner)
for the canonical EditForm + form-overlay composition pattern.

> See also: [references/spinner-template.md](references/spinner-template.md)
> (custom visuals + composition) and
> [references/spinner-overlay.md](references/spinner-overlay.md)
> (full-page, region, modal overlays).

## Minimal Example

```razor
@using Syncfusion.Blazor.Toolkit

@if (isLoading)
{
    <SfSpinner @bind-Visible="@isLoading" Label="Loading data..." />
}

@code {
    private bool isLoading = true;
}
```

For installation, services, visibility control, labels, CSS classes, and
size/positioning see [references/spinner-implementation.md](references/spinner-implementation.md).

---

## Documentation and Navigation Guide

| Need | Read |
|---|---|
| Setup, services, theme, visibility, labels, CSS, sizing | [references/spinner-implementation.md](references/spinner-implementation.md) |
| `OnOpen` / `OnClose` / `Created` / `Destroyed`, cancellation, templates | [references/spinner-events-customization.md](references/spinner-events-customization.md) |
| WCAG / keyboard / screen reader | [references/accessibility-best-practices.md](references/accessibility-best-practices.md) |
| Branded visuals, `SpinType`, CSS variables | [references/spinner-template.md](references/spinner-template.md) |
| Overlay patterns (page / region / modal / scroll-lock) | [references/spinner-overlay.md](references/spinner-overlay.md) |

---

## Next Steps

1. **Setup:** [references/spinner-implementation.md](references/spinner-implementation.md)
2. **Overlay patterns:** [references/spinner-overlay.md](references/spinner-overlay.md)

**Demo:** https://blazor.syncfusion.com/demos/toolkit/spinner
