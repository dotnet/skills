---
license: MIT
name: dialog-vs-tooltip-decision-matrix
description: >
  Decide between SfDialog and SfTooltip in the Syncfusion Blazor Toolkit
  with side-by-side comparison, lifecycle differences, and trigger-mode
  semantics. Required reading before either popup component is generated.
---

# Dialog vs Tooltip Decision Matrix

Both `SfDialog` and `SfTooltip` render a "popup" overlay, but they have
diametrically opposite UX semantics. Misusing them is the most common
popup-component bug.

## Quick decision

```
Need to block the user with required input or confirmation?
├─ Yes → SfDialog
└─ No
   ├─ Need interaction (a button in the popup)?
   │  └─ ❌ Neither — use SfPopover (not in this skill)
   └─ Just informational text?
      ├─ Triggered by hover / focus → SfTooltip
      └─ Triggered by click explicitly → SfTooltip with OpensOn="Click"
```

## Side-by-side

| Property | `SfDialog` | `SfTooltip` |
|---|---|---|
| Default trigger | Manual (`Visible`) | Hover / focus |
| Block UI? | Yes (with `IsModal`) | No |
| Focus trap | When `IsModal=true` | None |
| Close-on-Escape ✓ | `CloseOnEscape="true"` | Always (Escape is global) |
| Render strategy | Inline | Portal-style (renders to body root) |
| Light dismiss (click outside) | Optional via `CloseOnEscape` only | Auto on blur |
| `aria-modal` | Auto when `IsModal=true` | `aria-describedby` only |
| Default ARIA announcement | `role="dialog"` | `aria-describedby` |
| SSR-safe? | Yes (with `AllowPrerender=false`) | Fake DOM only — does **not** render in Static SSR |
| Position | Centered (configurable via `X`/`Y`) | 12 positions via `Position` enum |
| Animation | Yes (fade, slide, zoom) | Yes (fade, slide, none) |
| Buttons? | Yes (`DialogButtons`) | No |
| Form inputs | Yes | No — produces awkward patterns |
| Async / await inside | Yes (lifecycle methods) | Yes (`OnOpen`/`OnClose`) |
| Typical size | Modal: 320–640px; full-screen: 100% | 1–4 lines of text |
| Default duration | Indefinite | 100–300ms based on pointer movement |

## Lifecycle differences

`SfDialog`:

```text
Visible=false (initial)
   └─> user clicks "Open" → Visible=true
       ├─> OnOpen event (cancelable)
       ├─> OnAfterRender → component added to DOM
       └─> OnOpened event (no cancel)

visible=true (steady state)
   ├─> user clicks OK / Cancel / X → Visible=false
   ├─> CloseOnEscape → Visible=false
   ├─> OnClose event (cancelable)
   └─> OnAfterRender → component removed from DOM
```

`SfTooltip`:

```text
trigger element rendered
   └─> pointer enter or focus → tooltip appears
       └─> pointer leave or blur → tooltip disappears

   Tooltip is NEVER in the DOM when invisible — no "OnOpen"/"OnClose" fires.
   To intercept use:
     OnOpen  ← before append (BUT: in default impl, tooltip only opens
                on user interaction; programmatic Show() is the public API)
     OnClose ← before remove
```

**Note:** `SfTooltip` doesn't fire `OnOpen`/`OnClose` for the default hover
lifecycle. If you need a hook, call `Show()` / `Hide()` explicitly with the
public methods. Manual control via `@bind-Visible` is also supported.

## Trigger modes (`OpensOn`)

| Value | Trigger |
|---|---|
| `OpensOn.Auto` | Hover OR focus (default) |
| `OpensOn.Hover` | Pointer enter only |
| `OpensOn.Focus` | Focus only (keyboard users) |
| `OpensOn.Click` | Pointer down only |
| `OpensOn.Custom` | Manual `Show()` / `Hide()` |

**Best practice:** Default to `Auto` for icon buttons and links. Use
`Click` for non-interactive triggers (wrapping a static `<p>`). Use
`Custom` only when you need hooks — premature programmatic control is
over-engineering.

## Position enum (12 slots)

```text
TopLeft       TopCenter       TopRight
LeftCenter                                    RightCenter
BottomLeft    BottomCenter    BottomRight
```

`SfTooltip`: `Position="TooltipPosition.TopCenter"` (full enum name).

`SfDialog`: positions are configured via the `X` / `Y` numeric offsets
and `IsModal`.

## Animation parity

| Animation | `SfDialog` | `SfTooltip` |
|---|---|---|
| `Fade` | `DialogEffect="Fade"` | `TooltipAnimation="{ Effect=Fade }"` |
| `Slide` (left/right) | `DialogEffect="SlideLeft"` etc. | `SlideTop`, `SlideBottom`, etc. |
| `Zoom` | `DialogEffect="Zoom"` | `Effect="Zoom"` |
| `None` | `DialogEffect="None"` | `Effect="None"` |

The two components expose different enum shapes — code generation must use the
correct one per component.

## Accessibility matrix

| Need | `SfDialog` | `SfTooltip` |
|---|---|---|
| `<label>` association | Inside the dialog | Use `aria-describedby` on the trigger |
| Focus on open | First focusable child | Trigger element |
| Focus on close | Returns to last focused | Trigger element |
| `aria-modal` | Auto | Never |
| `role` | `dialog`, `alertdialog` if `IsModal=true` and only one button | `tooltip` (semantic) |
| Screen reader announcement | Dialog title | Tooltip content |
| Tab between trigger and content | Tab moves into dialog | Tab leaves trigger entirely |

`role="tooltip"` is the only role a tooltip may have. Don't make a dialog
with `role="tooltip"` — it loses `aria-modal` semantics.

## Common anti-patterns

### ◾ Dialog as a tooltip

```razor
<!-- ❌ WRONG: User just wanted to show help text. -->
<SfButton @onclick="@(() => isHelpVisible = true)">?</SfButton>
<SfDialog @bind-Visible="isHelpVisible"
          IsModal="true"
          Header="Help">
    Clear your browser cache and reload.
</SfDialog>
```

This blocks the page with a modal that only contains informational text. Use
`SfTooltip` instead:

```razor
<!-- ✅ RIGHT -->
<SfTooltip Content="Clear your browser cache and reload.">
    <SfButton>?</SfButton>
</SfTooltip>
```

### ◾ Tooltip for a confirmation

```razor
<!-- ❌ WRONG: Tooltip can't block confirmation. -->
<SfTooltip Content="Are you sure? (Click again to confirm)">
    <SfButton @onclick="@Submit">Submit</SfButton>
</SfTooltip>
```

Use `SfDialog`:

```razor
<!-- ✅ RIGHT -->
<SfDialog @bind-Visible="@showConfirm" IsModal="true"
          Header="Confirm">
    <DialogTemplates>
        <Content>
            <p>Are you sure?</p>
        </Content>
    </DialogTemplates>
    <DialogButtons>
        <DialogButton Content="Submit" OnClick="@Submit" />
        <DialogButton Content="Cancel" />
    </DialogButtons>
</SfDialog>
```

### ◾ Tooltip on a `disabled` element

`disabled="true"` elements don't fire pointer events. Wrap in a span:

```razor
<!-- ❌ Tooltip won't appear because the button doesn't fire mouseenter -->
<SfTooltip Content="Can't submit yet">
    <SfButton Disabled="true">Submit</SfButton>
</SfTooltip>

<!-- ✅ Wrap with a focus-receiving span -->
<SfTooltip Content="Can't submit yet" OpensOn="OpensOn.Custom">
    <span tabindex="0">
        <SfButton Disabled="true">Submit</SfButton>
    </span>
</SfTooltip>
```

(For accessibility, render a fallback explanation *inside* the disabled
button instead — the tabindex=0 wrapper is the workaround only when tooltip
absolutely must appear on a disabled control.)

### ◾ Dialog rendered inside a form button

`SfDialog` rendered inside `<DialogButtons>` of another dialog is fragile
because the nested-portal positioning needs an explicit z-index. Stack them
in parallel and toggle via two flags.

## Decision cheat-sheet

| Need | Component |
|---|---|
| Confirm destructive action | `SfDialog` (modal) |
| Show a form for input | `SfDialog` |
| Show brief informational text on hover | `SfTooltip` |
| Help text for an icon button | `SfTooltip` |
| Long-form content with multiple sections | `SfDialog` |
| Keyboard-shortcut hint | `SfTooltip` (`content="Ctrl+S"`) |
| Async progress notification | Use `SfSpinner` (overlay), not `SfTooltip` |
| Action menu attached to point-of-click | Use `SfMenuButton` / `SfContextMenu` — not in this skill |