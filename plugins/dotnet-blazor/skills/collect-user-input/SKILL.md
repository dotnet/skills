---
license: MIT
name: collect-user-input
description: Build forms, validate data, and react to user input in Blazor. USE FOR adding forms, search boxes, filter panels, inline editing, data-entry UI, file uploads, validation (annotations or custom), handling form submissions, and binding input controls. Covers EditForm, built-in input components, DataAnnotationsValidator, custom validation, SSR form patterns (SupplyParameterFromForm, FormName, AntiforgeryToken, Enhance), and @bind for simple interactive controls. DO NOT USE for project scaffolding (see create-blazor-project) or prerendering issues (see support-prerendering).
---

# Collect User Input

## Step 1 — Read the Project's AGENTS.md

Check `AGENTS.md` for **Interactivity Mode** and **Interactivity Scope**. This determines which form patterns apply:

| Mode | Form mechanism |
|------|---------------|
| None (Static SSR) | `EditForm` with `FormName` + `[SupplyParameterFromForm]`. No `@bind`, no `@onchange`. |
| Server | `EditForm` with `@bind-Value`. Full interactivity — real-time validation, dynamic UI. |
| WebAssembly | Same as Server, but validators needing server data must call APIs. |
| Auto | Same as WebAssembly — code must work in both browser and server. |

| Scope | Impact |
|-------|--------|
| Global | All forms are interactive. `FormName` only needed when explicitly opting a page to static SSR. |
| Per-page | Forms in static pages use `FormName` + `[SupplyParameterFromForm]`. Forms in `@rendermode` pages use `@bind-Value`. |

## EditForm Setup

`EditForm` requires **either** `Model` or `EditContext` — never both.

### Model-based (default)

```razor
<EditForm Model="Employee" OnValidSubmit="HandleSubmit" FormName="employee">
    <DataAnnotationsValidator />
    <ValidationSummary />

    <label>
        Name: <InputText @bind-Value="Employee!.Name" />
        <ValidationMessage For="() => Employee!.Name" />
    </label>

    <button type="submit">Save</button>
</EditForm>

@code {
    [SupplyParameterFromForm]
    private EmployeeModel? Employee { get; set; }

    protected override void OnInitialized() => Employee ??= new();

    private async Task HandleSubmit()
    {
        // Save Employee
    }
}
```

This single pattern works in **both** SSR and interactive modes:
- In SSR: `FormName` identifies the form, `[SupplyParameterFromForm]` binds POST data, `??=` initializes on GET.
- In interactive: `@bind-Value` provides two-way binding, `[SupplyParameterFromForm]` is ignored, `FormName` is harmless.

### EditContext-based (advanced)

Use when you need programmatic field tracking, dynamic validation rules, or manual `EditContext.Validate()` calls:

```csharp
private EditContext? editContext;
private EmployeeModel model = new();

protected override void OnInitialized()
{
    editContext = new EditContext(model);
}
```

```razor
<EditForm EditContext="editContext" OnValidSubmit="HandleSubmit" FormName="employee">
```

## Submit Handlers

| Handler | Fires when | Use when |
|---------|-----------|----------|
| `OnValidSubmit` | Validation passes | Standard forms with `DataAnnotationsValidator` |
| `OnInvalidSubmit` | Validation fails | Need custom handling for invalid state |
| `OnSubmit` | Always — validation is manual | Using `EditContext.Validate()` yourself |

`OnSubmit` cannot combine with `OnValidSubmit`/`OnInvalidSubmit`.

## Built-in Input Components

| Component | Binds to | Notes |
|-----------|----------|-------|
| `InputText` | `string` | Renders `<input type="text">` |
| `InputTextArea` | `string` | Renders `<textarea>` |
| `InputNumber<T>` | `int`, `double`, `decimal` | Renders `<input type="number">` |
| `InputDate<T>` | `DateTime`, `DateOnly`, `DateTimeOffset` | Renders `<input type="date">` |
| `InputCheckbox` | `bool` | Renders `<input type="checkbox">` |
| `InputSelect<T>` | `string`, enums, numeric types | Renders `<select>` |
| `InputRadioGroup<T>` | `string`, enums, numeric types | Wraps `InputRadio<T>` children |
| `InputFile` | `IBrowserFile` | File upload — interactive modes only |

All input components use `@bind-Value` for binding. Always wrap text in a `<label>` or use `id`/`for` attributes for accessibility.

### InputSelect with enum values

```razor
<InputSelect @bind-Value="Model!.Status">
    <option value="">-- Select --</option>
    @foreach (var value in Enum.GetValues<OrderStatus>())
    {
        <option value="@value">@value</option>
    }
</InputSelect>
```

### InputRadioGroup

```razor
<InputRadioGroup @bind-Value="Model!.Priority">
    @foreach (var p in Enum.GetValues<Priority>())
    {
        <label>
            <InputRadio Value="p" /> @p
        </label>
    }
</InputRadioGroup>
```

## Validation

### Data annotations

Define validation rules on the model:

```csharp
public class EmployeeModel
{
    [Required, StringLength(100)]
    public string? Name { get; set; }

    [Required, EmailAddress]
    public string? Email { get; set; }

    [Range(18, 99)]
    public int Age { get; set; }

    [Required]
    public string? Department { get; set; }
}
```

Add `<DataAnnotationsValidator />` inside `EditForm` — without it, annotation attributes are silently ignored.

Display errors with:
- `<ValidationSummary />` — all errors in a list
- `<ValidationMessage For="() => Model!.FieldName" />` — per-field inline errors

### Custom validator component

For server-round-trip validation (uniqueness checks, business rules):

```csharp
public class CustomValidator : ComponentBase
{
    [CascadingParameter]
    private EditContext? EditContext { get; set; }

    private ValidationMessageStore? messageStore;

    protected override void OnInitialized()
    {
        messageStore = new ValidationMessageStore(EditContext!);
        EditContext!.OnValidationRequested += (s, e) => messageStore.Clear();
        EditContext!.OnFieldChanged += (s, e) => messageStore.Clear(e.FieldIdentifier);
    }

    public void DisplayErrors(Dictionary<string, List<string>> errors)
    {
        foreach (var (field, messages) in errors)
        {
            foreach (var message in messages)
            {
                messageStore!.Add(EditContext!.Field(field), message);
            }
        }
        EditContext!.NotifyValidationStateChanged();
    }

    public void ClearErrors()
    {
        messageStore?.Clear();
        EditContext?.NotifyValidationStateChanged();
    }
}
```

Usage in a form:

```razor
<EditForm Model="Model" OnValidSubmit="HandleSubmit" FormName="register">
    <DataAnnotationsValidator />
    <CustomValidator @ref="customValidator" />
    <ValidationSummary />
    @* inputs *@
</EditForm>

@code {
    private CustomValidator? customValidator;

    private async Task HandleSubmit()
    {
        var errors = await RegistrationService.ValidateAsync(Model!);
        if (errors.Count > 0)
        {
            customValidator!.DisplayErrors(errors);
            return;
        }
        // proceed
    }
}
```

## React to Input Changes (Interactive Only)

### @bind:after

Run logic after a bound value changes:

```razor
<InputText @bind-Value="Model!.ZipCode" @bind:after="OnZipCodeChanged" />

@code {
    private async Task OnZipCodeChanged()
    {
        // Fetch city/state based on new zip code
        var location = await LocationService.LookupAsync(Model!.ZipCode);
        Model.City = location?.City;
        Model.State = location?.State;
    }
}
```

### @oninput for real-time filtering

```razor
<input type="text" @oninput="OnSearchInput" placeholder="Search..." />

@code {
    private string searchTerm = "";
    private List<Item> filteredItems = new();

    private void OnSearchInput(ChangeEventArgs e)
    {
        searchTerm = e.Value?.ToString() ?? "";
        filteredItems = allItems.Where(i =>
            i.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
```

## SSR-Specific Patterns

These apply when the form renders in Static SSR (mode = None, or per-page without `@rendermode`).

### SupplyParameterFromForm

Binds POST data to a property on form submission:

```csharp
[SupplyParameterFromForm]
private ContactModel? Contact { get; set; }

protected override void OnInitialized() => Contact ??= new();
```

**Critical:** The `??=` in `OnInitialized` is required. On GET the property is null — `??=` creates the model. On POST the framework populates it — `??=` preserves the posted values.

### FormName — multiple forms on one page

Each form needs a unique `FormName`:

```razor
<EditForm Model="Search" OnSubmit="DoSearch" FormName="search">...</EditForm>
<EditForm Model="Contact" OnValidSubmit="SaveContact" FormName="contact">...</EditForm>
```

Match `[SupplyParameterFromForm]` to its form:

```csharp
[SupplyParameterFromForm(FormName = "search")]
private SearchModel? Search { get; set; }

[SupplyParameterFromForm(FormName = "contact")]
private ContactModel? Contact { get; set; }
```

### Enhanced navigation for forms

Add `Enhance` for SPA-like form submissions without full page reload:

```razor
<EditForm Model="Model" OnValidSubmit="Save" FormName="quick" Enhance>
```

Enhanced forms submit via `fetch`, patch the DOM, and preserve scroll position. The page stays interactive-feeling even in SSR.

### Plain HTML forms

When using raw `<form>` instead of `EditForm` in SSR, add the antiforgery token manually:

```razor
<form method="post" @onsubmit="Submit" @formname="raw-form">
    <AntiforgeryToken />
    <input name="Model.Name" value="@Model?.Name" />
    <button type="submit">Send</button>
</form>
```

`EditForm` includes the antiforgery token automatically.

## File Upload

`InputFile` works in **interactive modes only** — not in Static SSR.

```razor
<InputFile OnChange="OnFileSelected" accept=".pdf,.jpg,.png" />

@code {
    private IBrowserFile? selectedFile;

    private async Task OnFileSelected(InputFileChangeEventArgs e)
    {
        selectedFile = e.File;

        // Read stream with size limit
        await using var stream = selectedFile.OpenReadStream(maxAllowedSize: 10 * 1024 * 1024);
        // Process stream — save to disk, upload to storage, etc.
    }
}
```

Stream size limits:
- **Server:** Default ~30 KB SignalR message size. Call `OpenReadStream(maxAllowedSize)` to increase. Large files stream over the circuit.
- **WebAssembly:** File is read in the browser. No SignalR limit, but memory constrained.

For multiple files:

```razor
<InputFile OnChange="OnFilesSelected" multiple />

@code {
    private async Task OnFilesSelected(InputFileChangeEventArgs e)
    {
        foreach (var file in e.GetMultipleFiles(maxAllowedFiles: 10))
        {
            await using var stream = file.OpenReadStream(maxAllowedSize: 10 * 1024 * 1024);
            // Process each file
        }
    }
}
```

## Prevent Double Submission

Disable the submit button while processing:

```razor
<button type="submit" disabled="@isSubmitting">
    @(isSubmitting ? "Saving..." : "Save")
</button>

@code {
    private bool isSubmitting;

    private async Task HandleSubmit()
    {
        isSubmitting = true;
        try
        {
            await SaveService.SaveAsync(Model!);
        }
        finally
        {
            isSubmitting = false;
        }
    }
}
```

## Custom Validation CSS

Replace the default `valid`/`invalid` CSS classes:

```csharp
public class BootstrapFieldCssClassProvider : FieldCssClassProvider
{
    public override string GetFieldCssClass(EditContext editContext, in FieldIdentifier fieldIdentifier)
    {
        var isValid = !editContext.GetValidationMessages(fieldIdentifier).Any();
        return editContext.IsModified(fieldIdentifier)
            ? (isValid ? "is-valid" : "is-invalid")
            : "";
    }
}
```

Apply to the form:

```csharp
protected override void OnInitialized()
{
    editContext = new EditContext(model);
    editContext.SetFieldCssClassProvider(new BootstrapFieldCssClassProvider());
}
```

## Don'ts

- Don't use `@bind` or `@oninput` in Static SSR forms — they require interactivity. Use `[SupplyParameterFromForm]` and `FormName`.
- Don't forget `Model ??= new()` in `OnInitialized` — the model is null on GET, populated on POST.
- Don't use `OnSubmit` together with `OnValidSubmit`/`OnInvalidSubmit` — they're mutually exclusive.
- Don't omit `<DataAnnotationsValidator />` — validation attributes are silently ignored without it.
- Don't omit `FormName` in SSR when a page has multiple forms — both forms will fire on any submission.
- Don't use `InputFile` in Static SSR — it requires an interactive render mode.
- Don't use both `Model` and `EditContext` on an `EditForm` — pick one.
- Don't forget `<AntiforgeryToken />` in plain `<form>` elements — the server rejects the POST without it.
