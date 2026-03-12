# Windows Forms and WPF Breaking Changes (.NET 9)

These changes affect projects using Windows Forms (`<UseWindowsForms>true</UseWindowsForms>`) or WPF (`<UseWPF>true</UseWPF>`).

## Windows Forms

### Source-Incompatible Changes

#### Changes to nullability annotations

**Impact: Medium.** Some WinForms API parameters changed nullability. Specifically, `IWindowsFormsEditorService.DropDownControl(Control)` parameter was previously nullable and is now non-nullable. Update implementations and call sites to match.

#### New security analyzers (WFO1000)

**Impact: Medium.** New analyzers enforce that properties in controls and `UserControl` objects have explicit serialization configuration via `DesignerSerializationVisibilityAttribute`, `DefaultValueAttribute`, or `ShouldSerialize[PropertyName]` methods. By default, the analyzer produces an **error**.

```
WFO1000: Property 'property' does not configure the code serialization for its property content.
```

**Fix:** Add appropriate serialization attributes to flagged properties — `DesignerSerializationVisibilityAttribute`, `DefaultValueAttribute`, or a `ShouldSerialize[PropertyName]` method.

> **Warning:** Do not suppress WFO1000 globally. This analyzer guards against insecure deserialization of control properties in the WinForms designer. Suppressing it can leave controls vulnerable to deserialization attacks through crafted designer files. If specific properties are intentionally excluded from serialization, use `[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]` on those properties rather than silencing the analyzer project-wide.

### Behavioral Changes

#### StatusStrip uses a different default renderer

**Impact: Low.** `StatusStrip.RenderMode` no longer defaults to `ToolStripRenderMode.System`. The visual appearance may differ. Set `RenderMode` explicitly to restore the previous look:
```csharp
statusStrip.RenderMode = ToolStripRenderMode.System;
```

> Note: This change was reverted in a .NET 9 servicing release.

#### PictureBox raises HttpClient exceptions

**Impact: Low.** `PictureBox` now raises `HttpRequestException` and `TaskCanceledException` instead of `WebException` when loading images from URLs fails. Update catch blocks:
```csharp
// Before
try { pictureBox.Load(url); }
catch (WebException) { }

// After
try { pictureBox.Load(url); }
catch (HttpRequestException) { }
catch (TaskCanceledException) { }
```

#### IMsoComponent support is opt-in

**Impact: Low.** WinForms threads no longer automatically register with `IMsoComponentManager` instances. To restore:
```xml
<ItemGroup>
  <RuntimeHostConfigurationOption Include="Switch.System.Windows.Forms.EnableMsoComponentManager" Value="true" />
</ItemGroup>
```

#### BindingSource.SortDescriptions doesn't return null

`SortDescriptions` now returns an empty collection instead of null.

#### ComponentDesigner.Initialize throws ArgumentNullException

`ComponentDesigner.Initialize` now throws `ArgumentNullException` for null input.

#### DataGridViewRowAccessibleObject.Name starting row index

The starting row index in accessible object names has changed.

#### No exception if DataGridView is null

`DataGridViewHeaderCell` no longer throws `NullReferenceException` when `DataGridView` is null.

## WPF

### Behavioral Changes / Source-Incompatible

#### `XmlNamespaceMaps` type change

**Impact: Low.** The backing property of `XmlAttributeProperties.XmlNamespaceMaps` changed from `String` to `Hashtable`. The `SetXmlNamespaceMaps` method now accepts `Hashtable` instead of `String`.

```csharp
// Before — passed string
XmlAttributeProperties.SetXmlNamespaceMaps(obj, someString);

// After — pass Hashtable
XmlAttributeProperties.SetXmlNamespaceMaps(obj, someHashtable);
```

`GetXmlNamespaceMaps` now returns `Hashtable` instead of `String`.
