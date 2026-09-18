---
name: winforms-expert
description: >-
  Create, modify, debug, or review Windows Forms applications, Forms, UserControls,
  custom controls, designer-generated code, layout, data binding, async UI code,
  accessibility, DPI, and dark mode. USE FOR: WinForms, System.Windows.Forms,
  InitializeComponent, *.Designer.cs, *.Designer.vb, Visual Studio Form Designer,
  TableLayoutPanel, BindingSource, Control.InvokeAsync, custom control serialization,
  or requests to make a Windows desktop UI designer-safe. DO NOT USE FOR: WPF,
  WinUI, .NET MAUI, Avalonia, console or web applications, or general C# work with
  no WinForms UI or designer concerns.
license: MIT
---

# WinForms Expert

## Purpose

Implement WinForms changes that compile, behave correctly at runtime, and remain editable in the
Visual Studio Designer. Treat `InitializeComponent` and `*.Designer.*` as a serialization format,
not ordinary application code; keep behavior in the main partial class and verify designer
usability whenever the environment permits.

## Execution Contract

Before editing, locate any task-specific validator, test script, or documented verification
command in the workspace. After editing:

1. Run that narrow validator first and fix every failure it attributes to the change.
2. Run the focused project build after the validator passes.
3. Report those commands separately from runtime UI and Designer round-trip checks.

A successful build never substitutes for an available layout, binding, serialization, or
designer-safety validator.

Use the repository's actual command and arguments; the sequence should look like:

```powershell
pwsh -NoProfile -File <task-validator.ps1> <scenario-arguments>
dotnet build <project> --nologo
```

## Boundaries

Use this skill for:

- Creating or changing a WinForms project, `Form`, `UserControl`, or custom control.
- Editing designer-generated layout, event wiring, resources, or component fields.
- Diagnosing a Form Designer load, serialization, scaling, binding, or toolbox problem.
- Implementing WinForms-specific async, MVVM, accessibility, DPI, or dark-mode behavior.

Do not use it to:

- Convert the application to another UI framework unless the user explicitly requests migration.
- Rewrite working generated code for style alone.
- Change the target framework, language version, SDK, package strategy, startup model, DPI mode,
  or application-wide theme unless required by the request.
- Claim that the Visual Studio Designer works when it was not actually opened and exercised.

## Inputs

| Input | Required | How to obtain it |
|---|---:|---|
| Requested UI behavior or defect | Yes | Use the user's request and reproduce the symptom when possible. |
| Project language and target framework | Yes | Inspect the project file; do not assume modern .NET or C#. |
| Existing Form/UserControl partial files | Yes | Find the main file, designer file, and matching `.resx`. |
| Repository conventions | Yes | Inspect nearby WinForms types, build scripts, analyzers, and package management. |
| Designer availability | No | Determine whether a compatible Visual Studio WinForms Designer can be opened. |

## Non-Negotiable Designer Contract

There are two code contexts:

| Context | Files | Rule |
|---|---|---|
| Designer serialization | `*.Designer.cs`, `*.Designer.vb`, `InitializeComponent` | Use simple, deterministic statements the designer can parse and regenerate. |
| Application behavior | Main partial class, services, view models | Use language features supported by the project's TFM and language version. |

In `InitializeComponent` and designer files:

- Keep control/component construction, property assignments, collection additions, layout
  suspension/resumption, resource application, and named event-handler wiring.
- Use class-level control/component fields. Do not add a control to `Controls`,
  `Items`, `Columns`, or another serialized collection through a local variable.
- Use named event handlers defined in the main partial file. Never use lambdas or anonymous
  delegates in generated event wiring.
- Do not add control flow (`if`, loops, `switch`, `try`/`catch`, `lock`, `await`, `goto`),
  local functions, expression-bodied logic, collection expressions, null-conditional or
  null-coalescing expressions, or other runtime decision logic.
- Do not add business logic, data loading, validation, service calls, or methods other than the
  established generated members such as `InitializeComponent` and `Dispose`.
- Preserve the generator's existing namespace style, qualification style, ordering, resources,
  and nullable treatment. Do not modernize generated syntax.
- Preserve existing constructors that the designer relies on. A designer-instantiated
  `Form`/`UserControl` must have a usable parameterless construction path unless the project uses
  an established custom designer pattern.
- Keep `BeginInit`/`EndInit`, `SuspendLayout`/`ResumeLayout`, and container ownership balanced.
- For C#, keep generated fields at class scope in the designer partial. For VB, preserve
  `Friend WithEvents` fields and prefer `Handles` clauses in the main file.

A safe `InitializeComponent` sequence is:

1. Instantiate controls and components.
2. Instantiate the `IContainer` when owned components require it.
3. Call required `BeginInit` and `SuspendLayout` methods.
4. Assign child control/component properties.
5. Add children to serialized collections and wire named handlers.
6. Configure the containing Form/UserControl after its children.
7. Call matching `EndInit`, `ResumeLayout`, and `PerformLayout` where appropriate.
8. Declare serialized backing fields in the generator's established location.

Move anything more complex to the main partial class, usually after `InitializeComponent` has run.

## Rules That Change the Answer

Use the symptom to choose one WinForms-specific repair. Do not list several plausible patterns when
the existing project and failure identify one.

| Symptom or request | Do | Never | Verify |
|---|---|---|---|
| A Form/UserControl builds but the Designer cannot instantiate it | Preserve the runtime constructor, add a designer-usable parameterless path, and defer dependency use until runtime load with an explicit design/null guard | Construct production services, open files, or query data from the designer path | The runtime composition root still supplies the real dependency; the design path reaches `InitializeComponent` without invoking it |
| Controls/components disappear, duplicate, or fail after Designer save/reopen | Restore the generated field/collection shape and balance every `BeginInit`/`EndInit` and `SuspendLayout`/`ResumeLayout` pair | Patch the symptom in `OnLoad` or move serialized controls into local variables | Save, close, reopen, and inspect the regenerated diff |
| A designer-created `Timer`, `BindingSource`, image list, or similar component outlives the Form | Create the `components` container and pass it to the component constructor | Add ad hoc disposal while leaving designer ownership inconsistent | Closing the Form disposes the container-owned component and the component remains designer-managed |
| Adding/removing list items does not refresh a bound WinForms list control | Use `BindingList<T>` or the repository's adapter that raises WinForms list-change notifications | Treat `ObservableCollection<T>` as a drop-in WinForms `DataSource` | Mutate the list after binding and observe the control update |
| Nested content clips at DPI, font, or localization changes | Fix the complete autosizing/docking chain from leaf through every parent to the Form | Increase one fixed `Size` or bypass a parent container | Exercise resize plus the relevant DPI/font/text expansion |
| UI work is posted but completion/errors are lost | Await the background operation; update controls on the captured WinForms context or await `InvokeAsync` when execution can be off-context; restore control state in `finally`; handle cancellation only when the operation has a real cancellation path | Use `_ =`, `BeginInvoke`, a dead cancellation catch, or an application-wide exception hook as the normal path | Exercise success and failure, plus cancellation only when the UI can actually request it |
| Text must be localizable | Use the existing `.resx` and `ComponentResourceManager.ApplyResources` serialization pattern | Leave fallback UI text hard-coded in `InitializeComponent` | Build, switch culture when possible, and perform a Designer save/reopen |
| A VB app needs startup, single-instance, or unhandled-UI hooks | Extend `ApplicationEvents.vb`; qualify `Microsoft.VisualBasic.ApplicationServices` event-argument types when ambiguous; restore, activate, and bring the existing `MainForm` forward; log `e.Exception`; set `e.ExitApplication = True` explicitly | Invent `Program.vb`, add `Sub Main`, replace generated startup, or leave post-error continuation implicit | The configured `StartupObject` and generated application file remain unchanged, no new entry point exists, and the final report states the exit choice |
| The workspace contains a task-specific validator or test script | Run the narrow repository-provided check after editing and before the generic build; treat its failure as evidence that the change is incomplete | Skip the specialized check because the project compiles, or claim a Designer round trip from static validation | Report the exact validator and build commands separately, then state whether runtime UI and Designer round-trip checks were actually available |

## Workflow

### 1. Establish the project constraints

1. Inspect the solution/project, target framework, language, nullable setting, package management,
   application startup, and existing build commands.
2. Identify whether the app targets modern .NET or .NET Framework. Preserve its current family
   unless migration is requested.
3. Read the complete partial-file set and `.resx` for every affected Form/UserControl before
   editing. Check base classes and nearby controls for repository conventions.
4. If the defect is designer-specific, record the exact load/serialization error and determine
   whether the problem occurs before making broad changes.

### 2. Plan the serialization boundary

1. Classify each change as serialized UI state or runtime behavior.
2. Put only designer-representable state in `InitializeComponent`.
3. Put conditional setup, dynamic content, data loading, conversions, validation, and service
   interaction in the main partial class or another runtime type.
4. Prefer the smallest designer diff. Do not reorder or reformat the whole generated file.
5. If editing generated code is avoidable, use the Designer or a runtime initialization method
   instead. If direct editing is necessary, follow the existing generated shape exactly.

### 3. Implement the UI structure

For new or substantially revised layouts:

- Prefer `TableLayoutPanel`, `FlowLayoutPanel`, `SplitContainer`, and nested UserControls over
  brittle absolute positioning.
- Divide complex screens into small layout regions. Avoid a single oversized grid.
- Prefer `AutoSize` for caption/single-line rows and columns, `Percent` for expandable content,
  and `Absolute` only for genuinely fixed-size content.
- Preserve the existing form's `AutoScaleMode`. For new forms, choose scaling deliberately based
  on the project conventions rather than copying coordinates from another DPI.
- Ensure an autosized child container participates in the full sizing chain; a fixed-height
  `Panel` or `GroupBox` inside an autosized row can still clip its contents.
- Set meaningful minimum sizes where a resizable or localized dialog could otherwise become
  unusable.
- Keep localized UI text in the project's resources and allow labels/buttons to grow.

For dialogs:

- Set `AcceptButton`, `CancelButton`, and appropriate `DialogResult` values.
- Validate the form as a whole when submitting. Do not trap users in a field by canceling every
  focus change.
- Dispose modal forms according to the project's ownership pattern.

### 4. Add behavior outside generated code

- Follow the project's language and style conventions; modern syntax belongs only in regular code.
- Keep UI-thread affinity explicit. Marshal control access from background work.
- A normal WinForms `async void` event handler resumes on its captured UI synchronization context
  after `await` unless that context was deliberately bypassed. Direct control access there is valid.
  Use and await `InvokeAsync` when code can continue off-context, or when the task/repository contract
  explicitly requires a marshaled operation; do not rely on an unawaited post.
- For modern .NET versions that support it, select the `Control.InvokeAsync` overload matching
  whether the delegate is synchronous/asynchronous and whether it returns a value. Await the
  returned operation; do not create fire-and-forget UI work.
- `async void` is acceptable for WinForms event handlers and overrides that must return `void`,
  but catch exceptions around awaited work, handle cancellation intentionally, and surface
  unexpected failures through the application's established error path.
- Do not use application-level exception hooks as a substitute for handling expected failures.
  `Application.ThreadException` covers UI-thread exceptions; `AppDomain.UnhandledException` is
  primarily a last-chance logging path and does not make continued execution safe.
- Preserve property allocation semantics. Do not change a cached property into
  `=> new ...` or vice versa without confirming lifetime and disposal behavior.

For application-wide startup:

- Preserve the current startup model. C# projects may configure application defaults in
  `Program.cs`; VB projects normally use the VB Application Framework and
  `ApplicationEvents.vb`, not a newly invented `Program.vb`.
- Use the APIs available to the actual TFM. Apply color mode or DPI defaults only when requested
  or consistent with the app's existing policy.
- Prefer `SystemAware` when maintaining the normal .NET WinForms DPI behavior. Use
  `PerMonitorV2` only when the application is designed and tested for per-monitor scaling.

### 5. Implement binding and serialization deliberately

For classic binding:

- Use `INotifyPropertyChanged` for mutable bound objects and `BindingList<T>` or an established
  adapter for list change notifications.
- Use `BindingSource` as the designer/runtime mediator when that matches the existing design.
- Put conversion logic in `Binding.Format` and `Binding.Parse` handlers rather than embedding
  executable logic in designer code.
- Treat one-way-to-source requests carefully: classic WinForms `Binding` does not provide a
  direct WPF-style mode. Document and test any workaround instead of implying native support.

For modern WinForms MVVM APIs, first confirm the TFM contains the requested API:

- Use `Control.DataContext` for ambient view-model context when available.
- Use `ButtonBase.Command`, `ToolStripItem.Command`, and `CommandParameter` when available.
- Keep view models in a UI-independent project when the solution already uses that separation or
  the requested change benefits from it; do not add a new architecture for a small fix.
- If design-time object data sources are required, preserve or add the project's established
  `.datasource` and `BindingSource` pattern and verify it in the Designer.

For custom `Component`/`Control` properties, choose one intentional CodeDOM serialization policy:

| Policy | Mechanism | Use |
|---|---|---|
| Serialize only when non-default | `[DefaultValue(...)]` or matching `ShouldSerializeX`/`ResetX` | Stable values with a meaningful default |
| Never serialize | `[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]` | Runtime-only, calculated, or unsupported state |
| Serialize content | `DesignerSerializationVisibility.Content` plus designer-compatible mutable content | Owned nested objects/collections intentionally edited in the property grid |

Do not combine contradictory policies. Test property-grid editing and save/reload behavior for
custom serialization changes.

### 6. Check usability and accessibility

- Set logical `TabIndex` order and verify keyboard-only traversal.
- Add unambiguous mnemonics where the product uses them.
- Set meaningful accessible names/descriptions for actionable controls when labels or context do
  not already provide them.
- Verify default focus, accept/cancel behavior, resizing, clipping, and localized text growth.
- Check at the DPI/theme combinations relevant to the change. System colors adapt to theme;
  hard-coded colors and owner-drawn controls do not.
- For owner drawing, `DataGridView`, icons, and custom painting, verify contrast in every supported
  theme rather than assuming dark-mode adaptation.

### 7. Validate in increasing-cost order

1. Re-read the designer diff and confirm every statement is serialization-safe and every referenced
   field/handler exists in the correct partial class.
2. Discover and run the repository's narrowest task-specific validation script or test when one is
   present. A generic build does not replace a designer/layout/binding validator.
3. Run the repository's smallest applicable restore/build/analyzer command. Use Visual Studio
   MSBuild when the project type or .NET Framework dependencies require it.
4. Run any remaining relevant automated tests.
5. Launch the affected UI when possible and exercise creation, load, resize, keyboard navigation,
   binding, async/error paths, and close/disposal behavior.
6. When Visual Studio with a compatible WinForms Designer is available:
   1. Open every changed Form/UserControl in the Designer.
   2. Confirm the design surface, toolbox integration, property grid, component tray, and inherited
      controls load without errors.
   3. Make a harmless reversible property change, save, close, reopen, and confirm the designer can
      serialize and reload the component.
   4. Revert only the harmless validation change, not the requested implementation.
   5. Build again after the serialization round trip.

## Observable Completion Criteria

A change is complete only when the checks applicable to the environment are observable:

- The project builds with no new compiler or analyzer errors.
- Relevant tests pass.
- The affected UI runs and the requested behavior is exercised, when launching is possible.
- No affected `InitializeComponent` contains unsupported runtime logic or unresolved fields,
  handlers, resources, or unbalanced layout/initialization calls.
- Changed custom properties survive a property-grid save/reopen round trip when applicable.
- Every changed Form/UserControl opens and reloads in the compatible Visual Studio Designer when
  designer access is available.

If Visual Studio, the Designer, Windows desktop access, a required workload, or a target runtime is
unavailable, report that validation as **not run**, state why, and list the exact checks that did
run. A successful build is not evidence that the Designer opens. Do not report the work as fully
designer-verified unless the design surface and serialization round trip were actually tested.

If a command fails:

1. Report the command and the first actionable error.
2. Distinguish a failure caused by the change from a missing tool/workload, incompatible platform,
   locked file, restore/network failure, or pre-existing repository failure.
3. Fix failures caused by the change and rerun the check.
4. Never replace a failed check with a success-shaped fallback or claim completion based only on
   source inspection.

Finish with a concise validation record:

- `Build:` exact command and result.
- `Runtime UI:` exercised behavior, or `not run` with the blocking reason.
- `Designer round trip:` Form/UserControl opened, edited, saved, closed, and reopened; or `not run`
  with the blocking reason.
- `Preserved:` project policy and generated/startup files intentionally left unchanged.

## Common Pitfalls

| Pitfall | Corrective rule |
|---|---|
| Treating `InitializeComponent` as normal modern C# | Keep it as simple designer serialization; move decisions and behavior to the main partial class. |
| Putting lambdas or local control variables in generated code | Use class-level fields and named handlers. |
| Editing only one partial file | Read and keep the main file, designer file, `.resx`, base type, and project metadata consistent. |
| Reformatting the entire designer file | Make a surgical diff that follows generated ordering and style. |
| Assuming a build proves designer health | Open, edit, save, close, and reopen the design surface. |
| Adding constructor dependencies to a designable type | Preserve a designer-usable parameterless path or an established custom designer solution. |
| Fixed-size nested containers clipping controls | Preserve an autosizing chain and test resize plus DPI scaling. |
| Using absolute colors for dark mode without testing | Prefer system colors or explicitly theme and contrast-test custom drawing. |
| Treating `ObservableCollection<T>` as a drop-in WinForms list source | Use `BindingList<T>` or an adapter that bridges collection and property notifications. |
| Assuming WPF binding or converter features exist | Use WinForms `Binding`, `BindingSource`, `Format`, and `Parse`; verify TFM-specific APIs. |
| Fire-and-forget UI marshaling or uncaught async event errors | Await `InvokeAsync`; catch and surface failures in `async void` UI entry points. |
| Inventing `Program.vb` for a VB application | Use the VB Application Framework and `ApplicationEvents.vb` conventions. |
| Changing DPI/theme/TFM/package policy during a UI fix | Preserve project policy unless the requested behavior requires and validates the change. |
