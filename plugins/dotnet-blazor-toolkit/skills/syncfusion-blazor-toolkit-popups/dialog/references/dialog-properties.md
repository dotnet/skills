---
license: MIT
name: dialog-properties
description: >
  Complete reference for Syncfusion Blazor Toolkit SfDialog properties,
  events, methods, and animation settings. Read when you need an
  authoritative signature or default value.
---

# SfDialog Properties & API Reference

The full surface of the `SfDialog` component. Use this when you need
canonical signatures, default values, or are debugging unexpected behavior.

## Properties

| Property | Type | Default | Purpose |
|---|---|---|---|
| `Visible` | `bool` | `false` | Show/hide the dialog |
| `@bind-Visible` | `bool` | — | Two-way binding for visibility (preferred) |
| `Header` | `string` | — | Top-bar text |
| `HeaderTemplate` | `RenderFragment` | — | Top-bar markup (overrides `Header`) |
| `ContentTemplate` | `RenderFragment` | — | Body markup |
| `Footer` / `FooterTemplate` | `string` / `RenderFragment` | — | Footer text or markup |
| `Width` | `string` | `"auto"` | Dialog width (CSS units; e.g. `"420px"`, `"60%"`) |
| `Height` | `string` | `"auto"` | Dialog height |
| `IsModal` | `bool` | `false` | Block underlying UI (installs `aria-modal` + focus trap) |
| `AllowDragging` | `bool` | `false` | Drag header to reposition |
| `EnableResize` | `bool` | `false` | Resize handle on edges |
| `AllowPrerender` | `bool` | `true` | Set `false` when dialog body depends on interactive content |
| `CloseOnEscape` | `bool` | `true` | Escape closes the dialog |
| `ShowCloseIcon` | `bool` | `false` | `×` icon in the header (required for mouse-only users) |
| `IsResponsive` | `bool` | `true` | Mobile-friendly reflow |
| `ZIndex` | `string` | `"1000"` | CSS stacking layer |
| `DialogAnimationSettings` | `DialogAnimationSettings` | `Fade` | `Effect` + duration (`DialogEffect.Fade`/`Zoom`/`Slide`*) |
| `CssClass` | `string` | — | Custom CSS classes applied to root element |

## Events

| Event | Signature | Cancellable? | Fires |
|---|---|---|---|
| `OnOpen` | `EventCallback<BeforeOpenEventArgs>` | yes | Before the dialog renders |
| `OnOpened` | `EventCallback<object>` | no | After first frame |
| `OnClose` | `EventCallback<BeforeCloseEventArgs>` | **yes** | Before dismissal (set `args.Cancel = true` to block) |
| `OnClosed` | `EventCallback<object>` | no | After dismissal |
| `OnOverlayClick` | `EventCallback<OverlayClickEventArgs>` | no | User clicked the backdrop |
| `VisibleChanged` | `EventCallback<bool>` | n/a | Two-way binding mirror (wired by `@bind-Visible`) |

## Methods (via `@ref`)

| Method | Returns | Purpose |
|---|---|---|
| `Show()` | `void` | Open programmatically (fires `OnOpen`) |
| `Hide()` | `void` | Close (fades then fires `OnClose`) |
| `SetModel(...)` | `void` | Update content from outside the dialog |
| `Refresh()` | `void` | Force re-render after content change |
| `DisposeAsync()` | `ValueTask` | Release JS interop references; call in `IAsyncDisposable` |

> **Programmatic control caveat:** `Show()` / `Hide()` and `@bind-Visible`
> compete — pick one mechanism. Mixing them causes "two-state controllers"
> fights (see Don'ts).

## Animation

`DialogAnimationSettings` exposes:

| Field | Type | Notes |
|---|---|---|
| `Effect` | `DialogEffect` | `Fade`, `Zoom`, `None`, `SlideLeft`, `SlideRight`, `SlideDown`, `SlideUp` |
| `Duration` | `int` (ms) | Default 400 |

Pick exactly one `Effect` per dialog. Mixing static `Effect=...` and
`Animation=...` constructions leads to rendering races (covered in Don'ts).

## Common Use Cases

| Scenario | Key Properties |
|---|---|
| Confirmation modal | `IsModal=true`, `ShowCloseIcon=true`, `CloseOnEscape=true`, `AllowPrerender=false` (for interactive content) |
| Form-in-modal | `<EditForm>` inside `DialogTemplates.Content`; `AllowPrerender=false` |
| Alert/info dialog | `IsModal=false`, `ShowCloseIcon=true` |
| Wizard | Sequential `@bind-Visible` states per step (don't nest `<SfDialog>`) |
| DialogService-driven dynamic dialog | Use the service-factory pattern (see [references/dialog-advanced.md](references/dialog-advanced.md)) |

## See Also

- [references/dialog-basics.md](references/dialog-basics.md) — installation, first modal walk-through
- [references/dialog-advanced.md](references/dialog-advanced.md) — service pattern, dynamic creator, wizard, cascading
- [../router/dialog-vs-tooltip-decision-matrix.md](../router/dialog-vs-tooltip-decision-matrix.md) — dialog vs tooltip decision matrix