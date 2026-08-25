---
license: MIT
name: syncfusion-blazor-toolkit-dialog
description: >
  Implement Syncfusion Blazor Toolkit SfDialog — modal and modeless windows with
  buttons, drag, resize, focus trap, ARIA semantics, and animation.
  USE FOR: confirmation modals, form-in-modal, full-screen dialogs, alert
  dialogs, settings panels, modals that block the underlying UI, and
  DialogService-driven dynamic creation.
  DO NOT USE FOR: brief informational text on hover (use the
  syncfusion-blazor-toolkit-tooltip skill), non-blocking progress overlays
  (use syncfusion-blazor-toolkit-notifications / SfSpinner), or as a navigation
  primitive.
metadata:
  author: "Syncfusion Inc"
  version: "1.0.0"
---

# Syncfusion Blazor Toolkit: Dialog (SfDialog)

`SfDialog` renders an overlay window that *blocks* the underlying page when
modal, or floats alongside it when modeless. It supports a header, body,
buttons, drag, resize, animation, and a service for dynamic creation.

## Core Rules

1. **Use `IsModal="true"` for any dialog that requires user input** — the
   framework installs the focus trap and `aria-modal`.
2. **Bind visibility with `@bind-Visible`, not `Visible=…` alone** — `SfDialog`
   needs `VisibleChanged` to mirror state.
3. **`DialogButtons` are declarative; one `OnClick` per `DialogButton`.** Use
   `OnClick="@(() => dialog.Close())"` to dismiss without inverting the state.
4. **`AllowPrerender="false"`** whenever the dialog body depends on
   interactive content (e.g., JS interop inside the dialog, `EditForm`
   validation, dynamic data). Default rendering in interactive render modes
   will double-mount the dialog.
5. **`CloseOnEscape="true"` is the default; re-declaring with the same value
   is harmless** but pairing with `ShowCloseIcon="true"` is required for
   mouse-only users to dismiss.
6. **`OnClose` is cancelable** — set `args.Cancel = true` to prevent close,
   useful for unsaved-changes warnings. `OnClosed` is non-cancelable and runs
   after dismissal.
7. **Programmatic lifecycle**: use a `@ref` (`SfDialog dlg`) and call
   `dlg.Show()` / `dlg.Hide()` — do not hand-toggle `bool isVisible` if you
   need to invoke `OnClose` first.

```razor
@using Syncfusion.Blazor.Toolkit.Popups
@implements IAsyncDisposable

<SfDialog @ref="confirm"
          @bind-Visible="@_showConfirm"
          IsModal="true"
          ShowCloseIcon="true"
          AllowPrerender="false"
          CloseOnEscape="true"
          Header="Confirm"
          Width="420px"
          OnClose="OnApproachClose">
    <DialogTemplates>
        <Content>
            <p>@_message</p>
        </Content>
    </DialogTemplates>
    <DialogButtons>
        <DialogButton Content="Yes" IsPrimary="true"
                      OnClick="@(() => OnDecideAsync(true))" />
        <DialogButton Content="No"
                      OnClick="@(() => OnDecideAsync(false))" />
    </DialogButtons>
</SfDialog>
```

## Decision Tree

```
Need a dialog?
├─ Single-shot static content?
│  └─ Inline <SfDialog @bind-Visible="…">
├─ Triggered by code from many places?
│  └─ DialogService (see dialog-advanced.md)
└─ Multi-step wizard?
   └─ Inline <SfDialog> + your own step state
```

## Documentation and Navigation Guide

| Need | Read |
|---|---|
| First modal / project setup | [references/dialog-basics.md](references/dialog-basics.md) |
| Properties, events, methods, animation surface (this skill's table-of-truth) | [references/dialog-properties.md](references/dialog-properties.md) |
| Service-pattern, dynamic creator, wizard, cascading integration | [references/dialog-advanced.md](references/dialog-advanced.md) |

For cross-component decision matrix (dialog vs tooltip vs popover), see
[../router/dialog-vs-tooltip-decision-matrix.md](../router/dialog-vs-tooltip-decision-matrix.md).

## Top 5 Properties You'll Use

| Property | Why it matters |
|---|---|
| `@bind-Visible` | Canonical visibility binding (works with `@bind-Visible` + `VisibleChanged`); not `Visible=...` alone |
| `IsModal` | Installs `aria-modal` + focus trap; required for any user-input dialog |
| `AllowPrerender` | Set `false` whenever the body depends on interactive content (JS interop, `EditForm`, dynamic data) |
| `ShowCloseIcon` | Mouse-only users have no other dismiss affordance; pair with `CloseOnEscape="true"` |
| `DialogAnimationSettings` / `Animation` | `DialogEffect` enum — pick exactly one (`Fade`/`Zoom`/`Slide*`/`None`) |

Full property/event/method/animation reference: [references/dialog-properties.md](references/dialog-properties.md).

## Common Patterns

### Pattern 1 — Inline confirmation modal (delete / unsaved-changes guard)

See the **Core Rules** snippet above for a full modal example.

### Pattern 2 — Programmatic lifecycle

```razor
@using Syncfusion.Blazor.Toolkit.Popups

<SfDialog @ref="dlg" AllowPrerender="false" />
<button @onclick="@(() => dlg.Show())">Open programmatically</button>
```

## Don'ts

| Anti-pattern | Symptom | Fix |
|---|---|---|
| Nested `<SfDialog>` inside another `<SfDialog>` | Render artifacts on overlay click; z-index collisions; nested focus trap locks browser | Use portals/z-index (inner > outer by ≥1000), or replace outer with stacked steps |
| Toggle `@bind-Visible` *and* call `Hide()` for the same dismissal | Second mechanism undoes the first; visuals flicker | Use only one mechanism: bind the bool, or call `Hide()` programmatically |
| `CloseOnEscape` used to cancel close | That flag toggles default behaviour; it doesn't intercept | Use the cancelable `OnClose` event with `args.Cancel = true` |
| `<SfDialog AllowDragging="true">` with `IsModal="true"` | Conflict — modal sets focus trap; dragging fights focus | Choose: free-form (no focus trap) or modal (no drag); rarely both |
| `<SfDialog>` containing `<form>` directly without binding the dialog lifecycle | Submit button doesn't post to the page EditContext model | Use `<EditForm Model="@X" OnValidSubmit="@Save">` inside `DialogTemplates`, and trigger save via button inside the form |
| `IsModal="false"` *and* no backdrop for destructive actions | User can click delete triggers elsewhere | `IsModal="true"` plus `ShowCloseIcon="true"` |
| `Visible="true"` initial in markup | Dialog opens on first paint — usually unintended auto-pop | Use `@bind-Visible` and toggle from a button |

> Don'ts covered by Anti-Pattern Workflows (double-binding, JS in `OnInitialized`, JS in `DialogTemplates`/`OnAfterRenderAsync`, no `DisposeAsync`) are not duplicated here — see the workflows below.

## Anti-Pattern Workflows

> **Note:** Snippets below tagged **Bad:** are intentionally minimal — namespace directives (`@using`), DI wiring, and other context are omitted to keep the failure-mode illustration short. The **Correct:** snippets include `@using` directives needed for copy-paste.

### Workflow 1 — Agent copies the dead `onConfirmCallback` snippet

**Anti-pattern** (matches an older SKILL.md `Modal Confirmation Dialog` snippet):
```razor
<SfDialog @bind-Visible="showConfirm" IsModal="true" Header="Confirmation">
    <DialogTemplates><Content><p>@confirmMessage</p></Content></DialogTemplates>
    <DialogButtons>
        <DialogButton Content="Yes" IsPrimary="true" OnClick="@OnConfirmYes"/>
        <DialogButton Content="No"  OnClick="@OnConfirmNo"/>
    </DialogButtons>
</SfDialog>

@code {
    private string confirmMessage = "";
    private Func<Task> onConfirmCallback;   // ❌ assigned, never invoked
    private async Task OnConfirmYes() { showConfirm = false; /* callback never called */ }
}
```

**Why it fails:** `onConfirmCallback` is set by `ShowConfirmation(...)` but
the button onclick never invokes it. Pressing "Yes" effectively does nothing.

**Correct — `EventCallback<T>`-based:**
```razor
@using Syncfusion.Blazor.Toolkit.Popups

<SfDialog @bind-Visible="@_showConfirm" IsModal="true" Header="Confirm"
          AllowPrerender="false" ShowCloseIcon="true">
    <DialogTemplates><Content><p>@_message</p></Content></DialogTemplates>
    <DialogButtons>
        <DialogButton Content="Yes" IsPrimary="true" OnClick="@(() => Decide(true))"/>
        <DialogButton Content="No" OnClick="@(() => Decide(false))"/>
    </DialogButtons>
</SfDialog>

@code {
    private bool _showConfirm;
    private string _message = "";
    [Parameter] public EventCallback<bool> OnDecision { get; set; }

    public void Ask(string message) { _message = message; _showConfirm = true; }
    private async Task Decide(bool accepted)
    {
        _showConfirm = false;
        await OnDecision.InvokeAsync(accepted);
    }
}
```

Parent wires `@bind-OnDecision` and gets a bool back.

### Workflow 2 — Agent calls `dlg.Show()` from `OnInitialized`

**Bad:**
```razor
@code {
    protected override void OnInitialized() { dialog.Show(); }
}
```

**Fix:**
```razor
@using Syncfusion.Blazor.Toolkit.Popups

<SfDialog @ref="dialog" AllowPrerender="false" />
<button @onclick="@(() => dialog.Show())">Open</button>

@code {
    SfDialog? dialog;

    protected override async Task OnAfterRenderAsync(bool first)
    {
        if (first && dialog is not null) await dialog.Show();
    }

    public async ValueTask DisposeAsync()
    {
        if (dialog is not null) await dialog.DisposeAsync();
    }
}
```

### Workflow 3 — Agent tries JS init inside `DialogTemplates` during prerender

**Bad:**
```razor
<SfDialog AllowPrerender="true">
    <DialogTemplates>
        <Content>
            @inject IJSRuntime JS
            <p>URL: @currentUrl</p>   <!-- JS throws on first paint -->
        </Content>
    </DialogTemplates>
</SfDialog>
@code {
    private string currentUrl;
    protected override void OnInitialized()
    {
        currentUrl = JS.InvokeAsync<string>("getHref").Result;
    }
}
```

**Fix:**
```razor
@using Syncfusion.Blazor.Toolkit.Popups

<SfDialog AllowPrerender="false">
    <DialogTemplates>
        <Content>
            @if (_url is null) { <p>Loading…</p> }
            else { <p>URL: @_url</p> }
        </Content>
    </DialogTemplates>
</SfDialog>

@code {
    private string? _url;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    protected override async Task OnAfterRenderAsync(bool first)
    {
        if (first) _url = await JS.InvokeAsync<string>("getHref");
    }
}
```

### Workflow 4 — Agent wants a confirmation modal that traps `OnClose`

**Correct:**
```razor
@using Syncfusion.Blazor.Toolkit.Popups

<SfDialog OnClose="@BlockIfDirty" @bind-Visible="@_open">
    <DialogTemplates>
        <Content><p>Save the document?</p></Content>
    </DialogTemplates>
    <DialogButtons>
        <DialogButton Content="Save"   OnClick="SaveFirst"/>
        <DialogButton Content="Discard" OnClick="Discard"/>
    </DialogButtons>
</SfDialog>

@code {
    private bool _dirty;
    private void BlockIfDirty(BeforeCloseEventArgs e)
    {
        e.Cancel = _dirty;     // user must explicitly Save or Discard
    }
}
```

**Button vs `OnClose` order:** `DialogButton.OnClick` fires *first*, then the
framework attempts the close (which triggers `OnClose`). If `OnClose` sets
`args.Cancel = true`, the dialog stays open — but the button's already-run
handler still executed. If your "Save" button must complete before the close
is decided, do the work inside the button's `OnClick` and *don't* rely on the
dialog still being open afterwards.

## Next Steps

1. **First modal:** [references/dialog-basics.md](references/dialog-basics.md)
2. **Service pattern / dynamic creator / wizard:** [references/dialog-advanced.md](references/dialog-advanced.md)
3. **Cross-skill decision matrix:** [../router/dialog-vs-tooltip-decision-matrix.md](../router/dialog-vs-tooltip-decision-matrix.md)

**Demo:** https://blazor.syncfusion.com/demos/toolkit/dialogs