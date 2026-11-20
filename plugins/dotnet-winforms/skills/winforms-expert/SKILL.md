---
name: winforms-expert
description: >-
  Create, modify, debug, or review Windows Forms applications only when the request
  contains a concrete Windows Forms marker. USE FOR: WinForms, Windows Forms,
  System.Windows.Forms, WinForms Form or UserControl designer files (*.Designer.cs or
  *.Designer.vb), Visual Studio WinForms Designer, TableLayoutPanel, BindingSource,
  DataGridView, Control.InvokeAsync, component-tray ownership, or custom control
  serialization. DO NOT USE FOR: WPF or WPF XAML, including Window.InputBindings,
  KeyBinding, commands, or DataContext; .NET MAUI; Avalonia; or any request where none
  of the listed Windows Forms markers is present.
license: MIT
---

# WinForms Expert

## Purpose

Implement WinForms changes that compile, behave correctly at runtime, and remain editable in the
Visual Studio Designer. Treat `InitializeComponent` and `*.Designer.*` as a serialization format,
not ordinary application code; keep behavior in the main partial class and verify designer
usability whenever the environment permits.

## Bundled guidance

This skill is self-contained. Its mandatory contracts apply without custom agents, AGENTS
templates, WARP, or other installed skills. Read only the references relevant to the task,
before implementing that part of the change:

| Task | Read |
|---|---|
| Designer construction, serialization repair, or no-op review | [Designer repair decisions](references/designer-repairs.md) |
| API availability, C#/VB startup, DPI policy, or modern modal data | [API and startup gates](references/api-and-startup.md) |
| Layout, dialog buttons, hosted views, editor minima, or window geometry | [Layout and dialog contracts](references/layout-and-dialogs.md) |
| Async events, cancellation, or cross-thread UI work | [UI threading](references/ui-threading.md) |
| Binding, component ownership, or custom property serialization | [Binding and serialization](references/binding-and-serialization.md) |
| Dark mode, icons, accessibility, or date/time editor choice | [Themes and accessibility](references/themes-and-accessibility.md) |

Resolve these paths relative to this `SKILL.md`, not the application workspace. If a required
reference is unavailable, report the missing resource and reduced coverage; do not silently
replace it with a sibling skill or claim that its checks were completed.

## Execution Contract

Before editing, list the workspace root and locate any task-specific validator, test script, or
documented verification command. Read its scenario selector and required arguments before choosing
an implementation. After editing:

1. Run that narrow validator first and fix every failure it attributes to the change.
2. Run the focused project build after the validator passes.
3. If the build or any later edit changes source, rerun the narrow validator.
4. Report those commands separately from runtime UI and Designer round-trip checks.

A successful build never substitutes for an available layout, binding, serialization, or
designer-safety validator. Do not finish after merely reading a validator or after a build succeeds.

## Boundaries

Do not use it to:

- Convert the application to another UI framework unless the user explicitly requests migration.
- Rewrite working generated code for style alone.
- Change the target framework, language version, SDK, package strategy, startup model, DPI mode,
  or application-wide theme unless required by the request.
- Claim that the Visual Studio Designer works when it was not actually opened and exercised.

## Inputs

Discover the project language/targets, partial files and `.resx`, base types, nearby conventions,
build/validation commands, and Designer availability. Reproduce the requested defect when possible;
do not ask the user for paths already discoverable in the workspace.

## Non-Negotiable Designer Contract

New application Forms, modal Forms, and composite UserControls must be partial types with
matching `.Designer.cs` or `.Designer.vb` files. Their parameterless design path calls
`InitializeComponent` without services, files, network access, async work, or graphics-device
initialization. Runtime constructor overloads should chain through that path. Preserve an
existing documented custom designer pattern when maintaining it; do not invent this exception
to excuse an undesignable new surface. Primitive custom Control/Component subclasses do not
need composite designer partials merely because they derive from Control.

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

Instantiate controls and any owned-components container before balanced initialization/layout
suspension; assign properties, add children, wire named handlers, then resume initialization/layout.
Keep new C# generated fields after generated methods (WFO3002), preserving established generator
conventions when maintaining existing files. Complex behavior belongs in the main partial.

## Workflow

### 1. Establish the project constraints

1. Inspect the project's targets, language, nullable setting, package/startup policy, and build commands.
2. Identify whether the app targets modern .NET or .NET Framework. Preserve its current family
   unless migration is requested.
   Read the API/startup reference before using modern members: a newer installed SDK does not
   add APIs to an older target framework. Check every target in multi-targeted projects.
3. Read the complete partial-file set and `.resx` for every affected Form/UserControl before
   editing. Check base classes and nearby controls for repository conventions.
4. If the defect is designer-specific, record the exact load/serialization error and determine
   whether the problem occurs before making broad changes.
5. Choose the matching bundled reference from the task table. If review finds no actual defect,
   leave tracked source/project files byte-for-byte unchanged and explain why; do not manufacture
   a cleanup, policy migration, or template rewrite.

### 2. Plan the serialization boundary

Classify each change as serialized UI state or runtime behavior. Follow the inline designer
contract and existing generated shape with the smallest diff. Runtime initialization is for
genuinely dynamic behavior, not a way to hide static controls from the Designer.

### 3. Implement the UI structure

Read the layout reference for new/repaired layouts. Use responsive regions with a complete
preferred-size chain; new hosted tab/wizard/view-part UserControls use AutoScaleMode.Inherit.
Preserve healthy Form scaling policy and localized resources. Precise command-button rows use
nested TableLayoutPanels, not FlowLayoutPanel.

For dialogs, assign AcceptButton/CancelButton and DialogResult; reject simple invalid OK closure
in OnFormClosing without trapping field focus or disabling Cancel. Stage async validation before
closing. Use target-supported DataContext or an explicit typed draft, commit only after successful
OK, and dispose according to the existing ownership pattern.

### 4. Add behavior outside generated code

1. Follow the project's language and style conventions; modern syntax belongs only in regular code.
2. For async work, read the UI-threading reference. Keep the operation awaitable, preserve captured
   UI-context behavior, and select a dispatch mechanism supported by the target.
3. For startup changes, read the API/startup reference. Preserve C# composition roots, the VB
   Application Framework, and established DPI/theme settings unless the task requires a change.
4. Preserve property allocation semantics. Do not change a cached property into `=> new ...` or
   vice versa without confirming lifetime and disposal behavior.

### 5. Implement binding and serialization deliberately

1. Read the binding/serialization reference when changing data sources, conversion, ownership,
   or property-grid behavior.
2. Keep classic BindingSource/list notifications intact; gate modern context/command members
   against the actual target. Do not introduce MVVM merely to repair a small classic-binding fix.
3. Align custom property defaults, runtime initialization, and one intentional CodeDOM policy.
4. Verify live updates and, when available, property-grid save/reopen behavior.

### 6. Check usability and accessibility

1. Read the theme/accessibility reference for theme APIs, command icons, or editor alternatives.
2. Verify keyboard traversal, mnemonics, meaningful accessible names, default focus, and dialog
   accept/cancel behavior.
3. Exercise resize, clipping, localized text growth, and the supported DPI/theme combinations.
   System colors alone do not prove that custom painting or every native child is theme-correct.

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
6. When a compatible Visual Studio Designer is available, open every changed Form/UserControl,
   check the surface/property grid/component tray/inherited controls, make a harmless property
   change, save/close/reopen, revert only that check, and build again.
7. If unavailable, report Designer/runtime validation as not run with the blocking reason.
   Build/static checks cannot substitute for opening and exercising the UI or Designer.

If a command fails:

1. Report the command and the first actionable error.
2. Distinguish a failure caused by the change from a missing tool/workload, incompatible platform,
   locked file, restore/network failure, or pre-existing repository failure.
3. Fix failures caused by the change and rerun the check.
4. Never replace a failed check with a success-shaped fallback or claim completion based only on
   source inspection.

Finish with a concise validation record:

```text
Build: <exact command and result>
Runtime UI: <exercised behavior, or not run with reason>
Designer round trip: <opened/edited/saved/closed/reopened, or not run with reason>
Preserved: <project policy and generated/startup files left unchanged>
```
