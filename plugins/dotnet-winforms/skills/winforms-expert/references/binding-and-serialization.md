# Binding and component serialization

## Classic binding

- Use INotifyPropertyChanged for mutable bound objects and BindingList or an established adapter
  for list-change notifications. ObservableCollection is not a drop-in WinForms list DataSource.
- Retain BindingSource as the designer/runtime mediator when that matches the existing design.
- Put conversion in Binding.Format and Binding.Parse handlers, not executable designer logic.
- Classic Binding does not expose a native WPF-style one-way-to-source mode. Document and test
  any requested workaround instead of implying native support.
- Preserve existing `.datasource` files, designer-managed BindingSource components, and resource
  patterns when adding design-time object data sources.

## Modern binding

Read the API/startup gates before using Control.DataContext, ButtonBase.Command,
ToolStripItem.Command, or CommandParameter. Confirm each member against the project's target.
Use classic binding/named handlers on older targets, not fabricated framework members.

## Owned components

Create the components IContainer when designer-owned Timer, BindingSource, ImageList, or similar
components need ownership, and pass it to their supported constructors. Keep one generated
disposal path. Do not add ad hoc disposal to mask inconsistent component-tray ownership.

## Custom properties

Choose one intentional CodeDOM serialization policy:

| Policy | Mechanism | Use |
|---|---|---|
| Serialize non-default values | DefaultValue metadata or matching ShouldSerialize/Reset methods | Stable values with meaningful defaults |
| Do not serialize | DesignerSerializationVisibility.Hidden | Runtime-only, calculated, or unsupported state |
| Serialize nested content | DesignerSerializationVisibility.Content with designer-compatible mutable content | Owned nested objects/collections intentionally edited in the property grid |

Import System.ComponentModel or qualify its attributes. Keep metadata and runtime initialization
directly aligned: `[DefaultValue(typeof(Color), "Yellow")]` must match direct Color.Yellow
initialization of the property/backing field, not a second constant/factory hiding the default.
Do not combine contradictory policies. Preserve cached versus newly allocated property semantics.

## Verify

Mutate bound properties and add/remove list items after binding; observe live control updates.
Verify owned components are disposed when the Form closes. For custom serialization changes,
edit the property grid, save, close, and reopen when the Designer is available. Build and static
checks are separate evidence, not a substitute for that roundtrip.
