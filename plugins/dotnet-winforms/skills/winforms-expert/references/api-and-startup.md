# API availability and startup

## Gate the target, not the installed SDK

1. Inspect every target framework, reference assembly/package, language version, and existing
   application configuration. A .NET 11 SDK compiling a .NET 8 project still exposes .NET 8 APIs.
2. Verify the exact declaring type, member, overload, and experimental status using the target's
   reference metadata and matching documentation.
3. Use a narrow compile/probe when a load-bearing API claim is uncertain. Build every supported
   target after implementation; a successful build for one target is not multi-target evidence.
4. If an API is missing, retain the supported pattern below. Do not add reflection shims, upgrade
   the TFM, or suppress experimental diagnostics merely to implement an unrelated UI fix.

| Feature | Availability check | Supported older-target path |
|---|---|---|
| Ambient context or non-MVVM modal draft | Confirm `Control.DataContext` is available on the target; Form inherits it | Existing typed draft property/constructor and BindingSource; no fake DataContext member |
| MVVM commands | Check `ButtonBase.Command`, `ToolStripItem.Command`, and the relevant `CommandParameter` members independently | Named Click handlers with explicit enabled/busy state |
| Async UI dispatch | `Control.InvokeAsync` is available from .NET 9; choose the exact sync/async and result overload | Captured WinForms context, or the project's awaitable legacy dispatch adapter |
| Async dialogs/forms | Verify `ShowAsync`/`ShowDialogAsync` and experimental status for the target | Existing modal ownership/lifetime pattern; never put ShowDialog on a worker thread |
| Application color mode | `Application.SetColorMode` is experimental in .NET 9 and non-experimental in .NET 10 | Existing theme policy, system colors, and explicitly tested custom painting |

An existing approved experimental opt-in can be preserved. A new opt-in or framework upgrade
requires an intentional scope decision and validation; unavailable functionality must be stated
explicitly, not reported as implemented.

## Preserve startup

- C#: retain the actual entry point and composition root. Keep application configuration before
  UI creation; do not add a second Main or change global defaults for a local layout fix.
- VB using the Application Framework: extend `ApplicationEvents.vb`; preserve `StartupObject`,
  generated application files, single-instance behavior, and existing MainForm composition.
  Do not invent `Program.vb` or `Sub Main`. Preserve a documented custom entry point if one
  already exists.
- For VB single-instance hooks, restore, activate, and bring the existing MainForm forward.
  Qualify `Microsoft.VisualBasic.ApplicationServices` event arguments when ambiguous.
- For VB unhandled-UI hooks, log `e.Exception` through the existing error path and explicitly
  choose `e.ExitApplication = True`; do not imply that continued execution is safe.

## Preserve DPI and designer policy

Keep the configured DPI-awareness mode, AutoScaleMode of established Form roots, application font,
and theme unless the requested fix concerns that policy. Do not default every existing project to
SystemAware or PerMonitorV2. A deliberate PerMonitorV2 migration needs real monitor-transition
validation, not just an updated startup call.

Keep existing documented inherited/custom designer patterns and serializers. New ordinary
Forms/modal Forms/composite UserControls still require designer partials and a safe design path.
Do not replace custom design-time support or generated VB startup as a side effect of modernization.

## Modal data without imposing MVVM

When the target supports DataContext, assign a typed editable draft to the modal Form's DataContext.
Initialize runtime binding after InitializeComponent, validate the draft, and commit only when the
caller receives successful OK. Cancel must leave live data unchanged. DataContext is a carrier,
not automatic validation or automatic two-way binding.

For older targets, use the existing explicit draft/BindingSource contract with the same ownership,
validation, and commit/cancel behavior. Do not invent an ambient-context compatibility layer.

Keep view models UI-independent when the solution already follows that separation or the request
benefits from it. Do not add a new architecture to make a small fix.

## Verification record

Record the actual target(s), selected API/pattern, and exact focused build result. State explicitly
when the requested modern feature is unavailable. Report startup/DPI/theme policy intentionally
preserved and distinguish compiled behavior from Designer/runtime checks.

## Sources

- [.NET 9 WinForms changes](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/whats-new/net90)
- [.NET 10 WinForms changes](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/whats-new/net100)
