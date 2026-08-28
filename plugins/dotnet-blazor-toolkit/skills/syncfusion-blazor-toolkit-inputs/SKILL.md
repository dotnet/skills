---
license: MIT
name: syncfusion-blazor-toolkit-inputs
description: >
  Implement Syncfusion Blazor Toolkit input components — TextBox, TextArea,
  NumericTextBox, Uploader, CheckBox, RadioButton, Switch.
  USE FOR: text, numeric, percentage, and currency inputs; multi-line
  text; Auto / AJAX file uploads with multi-file and validation; boolean
  and single-selection controls with two-way binding; WCAG and keyboard
  accessibility; integration with EditForm and EditContext.
  REQUIRES interactive render mode for `SfUploader` only; other inputs
  (TextBox, TextArea, NumericTextBox, CheckBox, RadioButton, Switch) work
  in Static SSR as well as interactive modes.
  DO NOT USE FOR: button controls (use syncfusion-blazor-toolkit-buttons),
  rich text editing (use SfRichTextEditor — not in this skill), date or
  time inputs (use syncfusion-blazor-toolkit-calendars).
compatibility: ".NET 8+, render-modes: Static SSR (text/checkbox/numeric/Switch); Server, WebAssembly, Auto (all)"
metadata:
  author: "Syncfusion Inc"
  version: "1.0.0"
---

# Syncfusion Blazor Toolkit - Input Components

> ⚙️ **Render mode:** `SfTextBox`, `SfTextArea`, `SfNumericTextBox<T>`, `SfCheckBox<T>`, `SfRadioButton<T>`, `SfSwitch<T>` work in all modes. **`SfUploader`** and **`InputFile` are NOT usable in Static SSR** — they require interactive render mode (Server, WebAssembly, or Auto @ .NET 8+). Read `AGENTS.md` before wiring file uploads.

## Core Rules

1. **Use `@bind-Value` for two-way binding**; use `ValueChanged` only when
   you need to inspect without binding. Never both.
2. **`TValue` and `TChecked` generic params must match the bound C# type.**
   `SfNumericTextBox<T>` requires `T : struct, IFormattable` — `T=string`
   compiles and crashes at runtime.
3. **`SfUploader` and `<InputFile>` are NOT usable in Static SSR** — the
   file-system path / JavaScript interop is unavailable.
4. **`SfUploader` requires a `SaveUrl`** (an HTTP endpoint); never point it
   at a static path on disk.
5. **`SfUploader` has a SignalR message-size limit (Server mode)** — call
   `OpenReadStream(maxAllowedSize)` to lift it for large uploads.
6. **`SfCheckBox<T>`**, **`SfRadioButton<T>`**, and **`SfSwitch<T>` use
   `TChecked` not `T`** in the generic parameter — be careful when
   copy-pasting between CheckBox and Switch.
7. **`SfNumericTextBox` accepts `Format` as a string** —
   `Format="C2"`, `Format="P"`, `Format="N0"`. The value is
   `CultureInfo.CurrentCulture` aware. **For currency, prefer a literal culture-locked pattern (e.g. `Format="$#,##0.00"`) to avoid locale-dependent UX.**
8. **Component parameter required** — use `[EditorRequired]` attribute on
   your `[Parameter]` declaration (compile-time check for component authors).
9. **Form-field required** — use `[Required]` (and other `DataAnnotations`
   attributes) on a model property, then include `<DataAnnotationsValidator />`
   inside `<EditForm>`. Without it, `[Required]` is silently ignored.
10. **`ID` and `Name` come from `HtmlAttributes`** if not explicitly supplied
    — don't rely on them being filled in.
11. **`ValueChange` fires before binding commits.** Use `@bind-Value:after`
    for post-commit validation or side effects.

## Don'ts

| Anti-pattern | Symptom | Fix |
|---|---|---|
| `SfCheckBox TChecked="string"` | Compile succeeds; runtime throws "Cannot convert null to bool" on first render | Use `TChecked="bool"` (or `int?` for 3-state) |
| `SfUploader` `AutoUpload="true"` on a static URL path | 404 / 405 from web server; upload "completes" but file is lost in client browser | Always POST to a controller endpoint that handles `IFormFile` and returns 200 |
| `<SfUploader>` / `<InputFile>` in a Static SSR page | Component never receives a file (`IBrowserFile` is null); prerender is always empty | Move to a `Server | WebAssembly | Auto` page or `@rendermode InteractiveServer`-only page |
| `SfNumericTextBox T="decimal" Format="N2"` for currency | Field shows `1,234.56` for en-US but `1.234,56` for de-DE — inconsistent UX | Use `Format="C2"` so the currency formatter takes culture into account; **or lock the format** (`Format="$#,##0.00"`) for culture-stable display |
| `<SfNumericTextBox @bind-Disabled="…" />` | Compile error or ignored — `Disabled` is a one-way attribute | Use `<SfNumericTextBox Disabled="@_busy" />` and toggle `_busy` manually |
| Two `SfNumericTextBox<T>` of different `T` in one `EditForm` row | Validation can bind to the wrong field; `@bind-Value` resolves to the first match | Set explicit `TValue` per input or split rows |
| Missing `<DataAnnotationsValidator />` in `<EditForm>` | All `[Required]`/`[StringLength]` rules silently ignored; form submits invalid data | Always include `<DataAnnotationsValidator />` as the first child of `<EditForm>` |
| Stripping `class="e-input"` from the rendered `<input>` | Numeric arrows / clear-button vanish; keyboard users lose step controls | Scope styling to parent (`<div>` / `<label>`) instead of the inner `<input>` |
| Binding `Value="@_text"` *and* `@bind-Value` | Property is set twice; one wins silently; `StateHasChanged` order is undefined | Pick exactly one |
| Setting `Min`/`Max` on `SfDatePicker` (not `SfNumericTextBox`) | That property doesn't exist on `SfDatePicker`; silent no-op | Use `Min`/`Max` on `SfDatePicker`, `Min`/`Max`/`Step` on `SfNumericTextBox` |

## Anti-Pattern Workflows

### Workflow 1 — Agent lifecycle: `OnInitialized` reads `IBrowserFile`

**Bad:**
```razor
@code {
    private IBrowserFile? _file;
    protected override void OnInitialized() { _file = HttpContext.GetFile(); }
}
```

**Why it fails:** `IBrowserFile` only exists inside an
`InputFileChangeEventArgs` from `<InputFile OnChange>`. Reading "out of band"
returns null and produces blank components.

**Correct:**
```razor
<InputFile OnChange="@LoadFile" accept=".pdf,.jpg,.png" />

@code {
    private IBrowserFile? _file;
    private async Task LoadFile(InputFileChangeEventArgs e)
    {
        _file = e.File;
        await using var stream = _file.OpenReadStream(maxAllowedSize: 10 * 1024 * 1024);
        // process stream safely
    }
}
```

### Workflow 2 — Agent sets `TChecked` to `string` because the doc looked similar

**Bad:**
```razor
<SfCheckBox TChecked="string" @bind-Checked="@_checked" Label="Agree" />
@code { private string _checked; /* user checks => binding expects bool */ }
```

**Correct:**
```razor
<SfCheckBox TChecked="bool" @bind-Checked="@_agreed" Label="Agree to terms" />
@code { private bool _agreed; }
```

### Workflow 3 — Agent omits `DataAnnotationsValidator` because the model has `[Required]`

**Bad:**
```razor
<EditForm Model="user" OnValidSubmit="SaveAsync">
    <InputText @bind-Value="user.Name" />
    <button type="submit">Save</button>
</EditForm>
```

**Why it fails:** `[Required]` is metadata; `<EditForm>` only enforces it
when `<DataAnnotationsValidator />` is present.

**Correct:**
```razor
<EditForm Model="user" OnValidSubmit="SaveAsync">
    <DataAnnotationsValidator />
    <ValidationSummary />
    <InputText @bind-Value="user.Name" />
    <button type="submit">Save</button>
</EditForm>
```

### Workflow 4 — Agent tries to disable just one field inside an `EditForm`

**Bad:** model has `bool _busy`; agent uses `Disabled="@_busy"` on **every**
input — now the form is fully inert, including the submit button. The user
cannot resubmit if there's a server-side error.

**Correct:**
```razor
<button type="submit" disabled="@_busy">@(_busy ? "Saving…" : "Save")</button>

@code {
    private bool _busy;
    private async Task SaveAsync()
    {
        _busy = true;
        try   { await UserService.SaveAsync(user); }
        catch { /* keep `_busy=false` so user can retry */ _busy = false; throw; }
        finally { _busy = false; }
    }
}
```

Inputs remain enabled; the Submit button reflects state.

### Workflow 5 — Agent wants to set default culture for `Format="C2"` currency

**Bad:** `Format="C2"` displays "£123.45" for `CultureInfo("en-GB")` but
"$123.45" for `en-US` — depends on the user's runtime culture. This is
rarely a feature; it's more often a bug.

**Correct:** wrap the entire `<CascadingValue Value="@culture">` around the
form, set `CultureInfo.DefaultThreadCurrentCulture = …` once in `Program.cs`,
or hard-code the culture in `Format`:

```razor
<SfNumericTextBox TValue="decimal"
                  Format="$#,##0.00"
                  Min="0"
                  Max="10000"
                  @bind-Value="@price" />
<!-- Always $ regardless of browser locale. -->
```

## Minimal Example

```razor
@using Syncfusion.Blazor.Toolkit.Inputs
@using System.ComponentModel.DataAnnotations

<EditForm Model="@formModel" OnValidSubmit="@Save">
    <DataAnnotationsValidator />
    <ValidationSummary />

    <label>Email:</label>
    <SfTextBox @bind-Value="@formModel.Email" />
    <ValidationMessage For="@(() => formModel.Email)" />

    <button type="submit">Save</button>
</EditForm>

@code {
    private LoginModel formModel = new();

    private void Save() { /* handle submit */ }

    public class LoginModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = "";
    }
}
```

---

## Documentation and Navigation Guide

| Component family | Read |
|---|---|
| Setup, CSS imports, theming | [references/getting-started-inputs.md](references/getting-started-inputs.md) |
| `SfTextBox`, `SfTextArea`, floating labels, focus/blur, validation states | [references/textbox-textarea.md](references/textbox-textarea.md) |
| `SfNumericTextBox<T>`, currency, percentage, Min/Max/Step, decimals, spin buttons | [references/numeric-currency.md](references/numeric-currency.md) |
| `SfUploader` (auto upload, multi-file, size/type validation, progress, drag-drop, errors) | [references/uploader.md](references/uploader.md) |
| `SfCheckBox<T>`, `SfRadioButton<T>`, `SfSwitch<T>`, grouped/conditional patterns | [references/checkbox-radio-switch.md](references/checkbox-radio-switch.md) |
| WCAG / keyboard / ARIA / labels / error announcement | [references/input-accessibility.md](references/input-accessibility.md) |

---

## Next Steps

1. **Setup:** [references/getting-started-inputs.md](references/getting-started-inputs.md)
2. **Component you're using:** one of the per-component references above
3. **Accessibility audit:** [references/input-accessibility.md](references/input-accessibility.md)

**Demo:** https://blazor.syncfusion.com/demos/toolkit/inputs
