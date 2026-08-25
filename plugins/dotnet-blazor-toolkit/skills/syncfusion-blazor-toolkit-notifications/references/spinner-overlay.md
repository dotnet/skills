---
license: MIT
name: spinner-overlay
description: >
  Render the Syncfusion Blazor Toolkit SfSpinner as a portal-style overlay
  (full-page, full-region, modal style, or focus-trapped modal). Covers
  z-index stacking, focus trap, aria-modal semantics, body scroll lock,
  and the difference between render in Static SSR vs interactive runtimes.
---

# Spinner Overlay Patterns

`SfSpinner` is often used as a *modal-style* overlay during long-running work.
Four overlay shapes are common; each has a known Composition recipe.

## 1. Full-page overlay (page-level lock)

Use when the entire page is being replaced or processed (e.g. "Submitting
order, please wait…").

```razor
@implements IDisposable

@if (_isProcessing)
{
    <div class="page-overlay" role="alert" aria-busy="true">
        <SfSpinner @bind-Visible="@_isProcessing"
                   ZIndex="9000"
                   Size="56"
                   Thickness="6"
                   Label="Submitting your order…" />
    </div>
}

@code {
    private bool _isProcessing;
    private async Task OnSubmit()
    {
        _isProcessing = true;
        await InvokeAsync(StateHasChanged);   // render the overlay NOW, before work
        try
        {
            await OrderService.SubmitAsync(order);
        }
        finally
        {
            _isProcessing = false;
        }
    }
}
```

```css
.page-overlay {
    position: fixed;
    inset: 0;
    background: rgba(255,255,255,0.72);
    z-index: 9000;
    display: flex;
    align-items: center;
    justify-content: center;
}
```

`InvokeAsync(StateHasChanged)` before the work runs is **critical** — otherwise
the overlay renders *after* the await, defeating its purpose.

`role="alert"` + `aria-busy="true"` keeps screen readers informed; combined
with `Label` on the spinner, this is the WCAG 2.1 minimum.

## 2. Section / region overlay (intra-page)

Use when one part of the page is loading (e.g. a chart area). The overlay
positioning is `relative` to the parent.

```razor
<div class="dashboard-region">
    <SfChart @ref="chartRef" />
    @if (isRefreshing)
    {
        <div class="region-overlay" aria-busy="true">
            <SfSpinner @bind-Visible="@isRefreshing"
                       Size="36"
                       Thickness="4"
                       Label="Refreshing data" />
        </div>
    }
</div>

<style>
  .dashboard-region { position: relative; min-height: 320px; }
  .region-overlay {
    position: absolute;
    inset: 0;
    background: rgba(255,255,255,0.6);
    display: flex; align-items: center; justify-content: center;
    backdrop-filter: blur(2px);
  }
</style>
```

**Rule:** The region container must have explicit `min-height` — otherwise the
overlay collapses around the spinner and looks broken.

## 2.5. Form-submission spinner (EditForm + SfSpinner)

Show spinner while a form submits. Pattern: render the form with the submit
button disabled; render the spinner in a sibling overlay that covers the form
during submission.

```razor
@using Syncfusion.Blazor.Toolkit

<div class="form-container">
    <EditForm Model="formData" OnSubmit="@HandleSubmit">
        <InputText @bind-Value="formData.Name" placeholder="Enter name" />
        <button type="submit" disabled="@isSubmitting">Submit</button>
    </EditForm>

    @if (isSubmitting)
    {
        <div class="form-overlay" role="alert" aria-busy="true">
            <SfSpinner @bind-Visible="@isSubmitting" Label="Submitting form..." />
        </div>
    }
</div>

@code {
    private FormModel formData = new();
    private bool isSubmitting = false;

    private async Task HandleSubmit()
    {
        isSubmitting = true;
        try
        {
            await Task.Delay(2000); // Simulate API call
        }
        finally
        {
            isSubmitting = false;
        }
    }

    private class FormModel
    {
        public string Name { get; set; }
    }
}
```

Two decisions to be aware of:

- **Spinner inside the form? No.** Keep the spinner adjacent (in the overlay) —
  nesting it inside the submit button breaks the click target.
- **Use `aria-busy="true"` on the overlay container** — the spinner itself
  isn't a live-region node; the wrapper carries the screen-reader announcement.

## 3. Modal-overlay (close-button disabled)

Combines `SfDialog` + `SfSpinner` for a "we're working, do not interact"
modal:

```razor
<SfDialog @bind-Visible="_showWorking"
          IsModal="true"
          ShowCloseIcon="false"
          AllowPrerender="false"
          Width="320px">
    <DialogTemplates>
        <Content>
            <div style="text-align:center;">
                <SfSpinner Visible="true"
                           Size="48"
                           Thickness="6"
                           Label="Working…" />
            </div>
        </Content>
    </DialogTemplates>
</SfDialog>
```

Notes:
- `ShowCloseIcon="false"` keeps the user from dismissing the modal
- `AllowPrerender="false"` prevents the spinner from rendering twice on dialog
  open in interactive render modes
- `IsModal="true"` traps focus inside the dialog

## 4. Body-scroll-lock overlay

Long-running work where the user must NOT scroll the underlying page
(checkout, autosave, etc.). Pair the overlay with manual scroll lock:

```razor
@implements IDisposable

@if (_isProcessing)
{
    <div class="page-overlay" …> … </div>
}

@code {
    private bool _isProcessing;
    protected override void OnAfterRender(bool firstRender)
    {
        if (_isProcessing) BodyLock();
        else BodyUnlock();
    }

    private void BodyLock()   => Document.Body.Style.Overflow = "hidden";
    private void BodyUnlock() => Document.Body.Style.Overflow = "";

    [Inject] private IJSRuntime JS { get; set; } = default!;
    // alternative: use IJSRuntime to set document.body.style.overflow
    public void Dispose() => BodyUnlock();
}
```

`OnAfterRender` is the only safe place to touch the DOM. `Dispose` releases
the lock on navigation away.

---

## Z-index discipline

Components live in stacked layers; pick a slot and stick to it:

| Layer | Z-index | Use |
|---|---|---|
| bage content | 0 | Default |
| Sticky header / footer | 100/200 | Navigation chrome |
| Floating action button | 500 | Primary screens |
| Tooltip / popover | 1500 | `SfTooltip`, `SfDropdown` |
| **Spinner overlay** | **9000** | Recommended |
| Modal dialog (`SfDialog`) | 9500 | Override the overlay if both are shown |
| Toast / Snackbar | 9800 | Async notifications |

Use a constants file (`@layers {` inline or `app.css`) to document the layer
stack — agents will reference it before assuming a number.

---

## Focus trap vs simple overlay

A **focus trap** is mandatory only when the user cannot interact with the
underlying page. Use the Syncfusion `SfDialog` with `IsModal="true"` — it
handles focus trap automatically.

For *non-interactive* overlays (the rest of the page is still visible but the
actions should be paused), use plain CSS and a manual flag — and **set
`tabindex="-1"` on overlay so it doesn't take focus**.

---

## Static SSR caveat

In `Static SSR` (`-int None`) render mode, `OnAfterRender` does **not** run on
the server. The overlay renders only on the client. Also: `IJSRuntime` is fine
in Static SSR but `document.body` access requires JS interop.

For Static SSR overlays, prefer element-only overlay (no body scroll lock):

```razor
<div class="page-overlay-static">
    <SfSpinner @ref="spinner"
               @bind-Visible="@isBusy"
               Label="Loading…" />
</div>
```

…where `page-overlay-static` is a small region (e.g. inside a `<section>`),
not a `position: fixed` overlay that locks the body.

---

## Animation performance

Overlay re-renders every `@bind-Visible` change. If the spinner *content*
changes during the overlay (e.g. label updates), do:

```razor
@if (_isProcessing)
{
    <SfSpinner @bind-Visible="@_isProcessing"
               @key="@_currentLabel"
               Label="@_currentLabel" />
}
@code {
    private string _currentLabel = "Loading…";

    private async Task Tick()
    {
        for (int i = 0; i <= 100; i += 25)
        {
            _currentLabel = $"Loading… {i}%";
            await Task.Delay(500);
            await InvokeAsync(StateHasChanged);
        }
    }
}
```

`@key="@_currentLabel"` forces a full remount on label change, smoothing the
text transition.

---

## Don'ts

- Don't double-bind — `Visible` and `@bind-Visible` together causes
  re-render storms
- Don't trap focus manually when `IsModal="true"` already does it
- Don't set `ZIndex="9999999"` — you'll fight toast/modal slots
- Don't use `position: fixed` overlays in Static SSR without first testing
  that the page-level layout doesn't break
- Don't unlock body scroll on modal close without also clearing the overlay
  (causes "stuck scroll")