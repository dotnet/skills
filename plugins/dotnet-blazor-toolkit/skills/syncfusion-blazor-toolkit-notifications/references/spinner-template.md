---
license: MIT
name: spinner-template
description: >
  Customize the Syncfusion Blazor Toolkit SfSpinner render with the Template
  parameter, SpinType visual styles, label rendering, Color mode, and CSS
  variables. REQUIRED for agents generating branded or non-default spinners.
---

# Spinner Template & Visual Customization

The default `SfSpinner` renders a Material-style 30×30 rotating arc. For branded
loading indicators, three knobs are exposed: `Template`, `Type`, `CssClass`.

## Anatomy

```
<spinner-root>            (CSS class e-spinner-pane)
  <spinner-inner>           (e-spinner-inner)
    <spinner-arc/>          (animated stroke; default)
    <label/>                (Label text)
  </spinner-inner>
</spinner-root>
```

Replacing any of these nodes requires the `Template` parameter.

---

## `Template` parameter (RenderFragment)

The `Template` fragment replaces the *whole inner area* — the arc, the label,
and any default content. **You are responsible for the animation.**

```razor
<SfSpinner @bind-Visible="isLoading">
    <Template>
        <div class="brand-spinner">
            <div class="brand-spinner__dot"></div>
            <div class="brand-spinner__dot"></div>
            <div class="brand-spinner__dot"></div>
        </div>
    </Template>
</SfSpinner>

<style>
  .brand-spinner { display: inline-flex; gap: 4px; }
  .brand-spinner__dot {
    width: 8px; height: 8px; border-radius: 50%;
    background: var(--brand-color, #5b6dcd);
    animation: brand-pulse 1.2s ease-in-out infinite;
  }
  .brand-spinner__dot:nth-child(2) { animation-delay: 0.15s; }
  .brand-spinner__dot:nth-child(3) { animation-delay: 0.3s; }
  @keyframes brand-pulse {
    0%, 80%, 100% { transform: scale(0.6); opacity: 0.5; }
    40% { transform: scale(1); opacity: 1; }
  }
</style>
```

**Rules:**
- The `Template` fragment does not include `Label` rendering; add your own
  `<span class="sr-only">Loading…</span>` for screen readers.
- Do not set `Label="…"` together with `<Template>` — duplicate announcements.
- `CssClass` is applied to the *outer* root and survives `Template` replacement.

---

## `Type` (SpinType) and visual styles

`SfSpinner.Type` accepts `SpinType.Default` (CSS rotating arc) or
`SpinType.Material3` (newer Material symbol). For non-default visuals use
`Template`.

```razor
<SfSpinner Type="SpinType.Material3" Label="Loading…" />
```

**Don't** wrap a `<Template>` + `<Label>` spinner inside another rotating
element — browsers cap concurrent animations at 100/element.

---

## `Size` and `Thickness` parameters

| Parameter | Type | Use |
|---|---|---|
| `Size` | `string` (e.g. `"24"`, `"36px"`, `"48"`) | Width AND height of the spinner (square) |
| `Thickness` | `string` (e.g. `"2"`, `"4"`, `"6px"`) | Width of the stroke arc |

```razor
<SfSpinner Size="48" Thickness="6" />
```

When `Size` is set to a CSS length the component scales its arc proportionally.
When no unit is provided, pixels are assumed.

---

## `Color` (color of the arc)

`SfSpinner` exposes `Color` for the arc stroke:

```razor
<SfSpinner Color="var(--brand-primary)" />
```

Accepts any CSS color string (`#hex`, `rgb()`, `var()`, named colors). When
unset, the arc inherits `currentColor` from the parent — that's usually
correct, but inside a button or disabled form element the arc may collapse to
`transparent`.

**Don't** rely on `Color="currentColor"` if the spinner is inside a disabled
context.

---

## CSS variables exposed by the component

| Variable | Purpose |
|---|---|
| `--e-spinner-arc-stroke` | Stroke color of the default arc (overrides `Color`) |
| `--e-spinner-arc-bg` | Background track color |
| `--e-spinner-arc-size` | Replaces `Size` |
| `--e-spinner-arc-thickness` | Replaces `Thickness` |
| `--e-spinner-zindex` | Replaces `ZIndex` |

```css
/* Override default colors without touching markup */
.branded-page {
  --e-spinner-arc-stroke: #ff5722;
  --e-spinner-arc-bg: rgba(0, 0, 0, 0.08);
}
```

---

## Accessibility with custom templates

Screen readers announce `Label` even when `Template` is set. To control
custom announcement copy:

```razor
<SfSpinner @bind-Visible="isLoading"
           Label="Submitting your order"
           Type="SpinType.Material3">
    <Template>
        <span aria-hidden="true">
            <div class="brand-spinner">…</div>
        </span>
    </Template>
</SfSpinner>
```

`aria-hidden="true"` on the visual template prevents duplicate announcements.
`Label` provides the announcement text for assistive tech.

**Don't** omit `Label` — silent spinners are an a11y failure.

---

## Composition patterns

### Inline (button-internal) spinner

```razor
<button disabled="@isSubmitting">
    @if (isSubmitting)
    {
        <SfSpinner Visible="true"
                   Size="14"
                   Thickness="2"
                   CssClass="btn-spinner"/>
    }
    Submit
</button>
```

`CssClass="btn-spinner"` is added next to the button, separated by display:
inline-block so it does not push the text to a new line.

### Skeleton-replacement spinner

```razor
@if (data is null)
{
    <SfSpinner @bind-Visible="@showSpinner"
               CssClass="skeleton-spinner"
               Label="Loading data"/>
}
else
{
    <DataTable Data="@data" />
}
```

`CssClass="skeleton-spinner"` lets you style the spinner container to occupy
the same bounding box as the eventual table — no layout shift.

### Page-level overlay

```razor
@if (showOverlay)
{
    <div class="page-overlay">
        <SfSpinner @bind-Visible="@showOverlay"
                   ZIndex="9000"
                   Size="56"
                   Thickness="6"
                   Label="Loading the next page…" />
    </div>
}
```

`ZIndex="9000"` ensures the overlay is on top of nav/header; pair with a
`<div class="page-overlay">` that has `position: fixed; inset: 0; background:
rgba(255,255,255,0.6);` to dim the underlying content.

---

## Don'ts

- Don't combine `<Template>` and `Label` to render the same text twice
- Don't set `Size="auto"` — invalid, will fall back to default 30×30
- Don't apply `display: flex` to the spinner root via CssClass — syncfusion
  applies it internally; override only with caution
- Don't put a spinner inside a disabled `SfButton` — the spinner itself
  inherits `pointer-events: none`, breaking mouseover hints