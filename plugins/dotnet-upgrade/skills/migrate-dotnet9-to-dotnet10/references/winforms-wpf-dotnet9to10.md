# Windows Forms and WPF Breaking Changes (.NET 10)

These changes affect projects using Windows Forms (`<UseWindowsForms>true</UseWindowsForms>`) or WPF (`<UseWPF>true</UseWPF>`).

## Windows Forms

### Source-Incompatible Changes

#### API obsoletions

Several Windows Forms APIs have been marked obsolete with custom diagnostic IDs. Follow the guidance in the warning message for each.

#### Applications referencing both WPF and WinForms must disambiguate MenuItem and ContextMenu types

If a project references both WPF and WinForms, the `MenuItem` and `ContextMenu` types are ambiguous. Use fully qualified names:

```csharp
// Before (ambiguous in .NET 10)
var item = new MenuItem("File");

// After
var item = new System.Windows.Forms.MenuItem("File");
// or
var item = new System.Windows.Controls.MenuItem();
```

#### Renamed parameter in HtmlElement.InsertAdjacentElement

The parameter name in `HtmlElement.InsertAdjacentElement` has changed from `orientation`. Calls that use named arguments with `orientation` will no longer compile; update them to use positional arguments:

```csharp
// Before — named arguments with old parameter name
element.InsertAdjacentElement(orientation: HtmlElementInsertionOrientation.BeforeBegin, newElement: newElement);

// After — use positional arguments
element.InsertAdjacentElement(HtmlElementInsertionOrientation.BeforeBegin, newElement);
```

### Behavioral Changes

#### TreeView checkbox image truncation

The checkbox rendering in `TreeView` controls has been adjusted, which changes text positioning. Visual appearance may differ slightly.

#### StatusStrip uses System RenderMode by default

`StatusStrip` now uses the system render mode by default instead of a custom renderer. The visual appearance may change. Set `RenderMode` explicitly to restore the previous look.

#### System.Drawing OutOfMemoryException changed to ExternalException

**Important:** Some `System.Drawing` operations that previously threw `OutOfMemoryException` now throw `ExternalException` (from `System.Runtime.InteropServices` — NOT `ArgumentException`). This reflects the actual GDI+ error code. Update catch blocks:

```csharp
// Before — only catching OutOfMemoryException
try { /* drawing operation */ }
catch (OutOfMemoryException) { /* handle */ }

// After — catch ExternalException (the new exception type in .NET 10)
try { /* drawing operation */ }
catch (ExternalException) { /* handle */ }
catch (OutOfMemoryException) { /* handle — for older runtimes */ }
```

## WPF

### Source-Incompatible / Behavioral Changes

#### Empty ColumnDefinitions and RowDefinitions are disallowed

Empty `<Grid.ColumnDefinitions/>` and `<Grid.RowDefinitions/>` elements in XAML now cause errors. Remove them if they don't contain any definitions:

```xml
<!-- Before (now causes error) -->
<Grid>
    <Grid.ColumnDefinitions/>
    <Grid.RowDefinitions/>
</Grid>

<!-- After -->
<Grid>
    <!-- Only include definitions if you have columns/rows to define -->
</Grid>
```

#### Incorrect usage of DynamicResource causes application crash

Incorrect `DynamicResource` usage that was silently ignored now causes crashes at runtime. Common issues:
- Using `DynamicResource` where `StaticResource` is required (e.g., in non-dependency-property contexts)
- Referencing resources that don't exist

Audit all `DynamicResource` usage in XAML and ensure each reference points to a valid resource and is used in a context that supports dynamic resources.
