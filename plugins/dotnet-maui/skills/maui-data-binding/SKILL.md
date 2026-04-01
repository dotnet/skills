---
name: maui-data-binding
description: >-
  Guidance for .NET MAUI XAML and C# data bindings — compiled bindings,
  INotifyPropertyChanged / ObservableObject, value converters, binding modes,
  multi-binding, relative bindings, fallbacks, and MVVM best practices.
  USE FOR: setting up compiled bindings with x:DataType, implementing
  INotifyPropertyChanged or CommunityToolkit ObservableObject, creating
  IValueConverter / IMultiValueConverter, choosing binding modes, configuring
  BindingContext, relative bindings, binding fallbacks, StringFormat,
  code-behind SetBinding with lambdas, and enforcing XC0022/XC0025 warnings.
  DO NOT USE FOR: CollectionView item templates and layouts (use
  maui-collectionview), Shell navigation data passing (use
  maui-shell-navigation), dependency injection (use maui-dependency-injection),
  or animations triggered by property changes (use .NET MAUI animation APIs).
---

# .NET MAUI Data Binding

Wire UI controls to ViewModel properties with compile-time safety, correct
change notification, and minimal overhead. Prefer compiled bindings everywhere
and treat binding warnings as build errors.

## When to Use

- Adding `x:DataType` compiled bindings to a new or existing page
- Implementing `INotifyPropertyChanged` or CommunityToolkit `ObservableObject`
- Creating or consuming `IValueConverter` / `IMultiValueConverter`
- Choosing the correct `BindingMode` for a control property
- Setting `BindingContext` in XAML or code-behind
- Using relative bindings (`Self`, `AncestorType`, `TemplatedParent`)
- Applying `StringFormat`, `FallbackValue`, or `TargetNullValue`
- Writing AOT-safe code bindings with `SetBinding` and lambdas (.NET 9+)

## When Not to Use

- **CollectionView layouts / templates** — use the `maui-collectionview` skill
- **Shell navigation parameters** — use the `maui-shell-navigation` skill
- **Service registration / DI** — use the `maui-dependency-injection` skill
- **Property-change-triggered animations** — use built-in [.NET MAUI animation APIs](https://learn.microsoft.com/dotnet/maui/user-interface/animation/basic)

## Inputs

- A .NET MAUI project targeting .NET 8 or later
- XAML pages or C# code-behind where bindings are declared
- A ViewModel class (or plan to create one)

---

## Compiled Bindings — x:DataType Placement

Compiled bindings are **8–20× faster** than reflection-based bindings and are
required for NativeAOT / trimming. Enable them with `x:DataType`.

### Placement rules

Set `x:DataType` **only where `BindingContext` is set**:

1. **Page / View root** — where you assign `BindingContext`.
2. **DataTemplate** — which creates a new binding scope.

Do **not** scatter `x:DataType` on arbitrary child elements. Adding
`x:DataType="x:Object"` on children to escape compiled bindings is an
anti-pattern — it disables compile-time checking and reintroduces reflection.

```xml
<!-- ✅ Correct: x:DataType at the page root -->
<ContentPage xmlns:vm="clr-namespace:MyApp.ViewModels"
             x:DataType="vm:MainViewModel">
    <StackLayout>
        <Label Text="{Binding Title}" />
        <Slider Value="{Binding Progress}" />
    </StackLayout>
</ContentPage>

<!-- ❌ Wrong: x:DataType scattered on children -->
<ContentPage x:DataType="vm:MainViewModel">
    <StackLayout>
        <Label Text="{Binding Title}" />
        <Slider x:DataType="x:Object" Value="{Binding Progress}" />
    </StackLayout>
</ContentPage>
```

### DataTemplate always needs its own x:DataType

```xml
<CollectionView ItemsSource="{Binding People}">
    <CollectionView.ItemTemplate>
        <DataTemplate x:DataType="model:Person">
            <Label Text="{Binding FullName}" />
        </DataTemplate>
    </CollectionView.ItemTemplate>
</CollectionView>
```

### Enforce binding warnings as errors

| Warning | Meaning |
|---------|---------|
| **XC0022** | Binding path not found on the declared `x:DataType` |
| **XC0023** | Property is not bindable |
| **XC0024** | `x:DataType` type not found |
| **XC0025** | Binding used without `x:DataType` (non-compiled fallback) |

Add to the `.csproj`:

```xml
<WarningsAsErrors>XC0022;XC0025</WarningsAsErrors>
```

---

## Binding Modes

Set `Mode` explicitly **only** when overriding the default. Most properties
already have the correct default:

| Mode | Direction | Use case |
|------|-----------|----------|
| `OneWay` | Source → Target | Display-only (default for most properties) |
| `TwoWay` | Source ↔ Target | Editable controls (`Entry.Text`, `Switch.IsToggled`) |
| `OneWayToSource` | Target → Source | Read user input without pushing back to UI |
| `OneTime` | Source → Target (once) | Static values; no change-tracking overhead |

```xml
<!-- ✅ Defaults — omit Mode -->
<Label Text="{Binding Score}" />
<Entry Text="{Binding UserName}" />
<Switch IsToggled="{Binding DarkMode}" />

<!-- ✅ Override only when needed -->
<Label Text="{Binding Title, Mode=OneTime}" />
<Entry Text="{Binding SearchQuery, Mode=OneWayToSource}" />

<!-- ❌ Redundant — adds noise -->
<Label Text="{Binding Score, Mode=OneWay}" />
<Entry Text="{Binding UserName, Mode=TwoWay}" />
```

---

## BindingContext and Property Paths

Every `BindableObject` inherits `BindingContext` from its parent unless
explicitly set. Property paths support dot notation and indexers:

```xml
<Label Text="{Binding Address.City}" />
<Label Text="{Binding Items[0].Name}" />
```

Set `BindingContext` in XAML:

```xml
<ContentPage xmlns:vm="clr-namespace:MyApp.ViewModels"
             x:DataType="vm:MainViewModel">
    <ContentPage.BindingContext>
        <vm:MainViewModel />
    </ContentPage.BindingContext>
</ContentPage>
```

Or in code-behind (preferred with DI):

```csharp
public MainPage(MainViewModel vm)
{
    InitializeComponent();
    BindingContext = vm;
}
```

---

## INotifyPropertyChanged and ObservableObject

### Manual implementation

```csharp
public class MainViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _title = string.Empty;
    public string Title
    {
        get => _title;
        set
        {
            if (_title != value)
            {
                _title = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
            }
        }
    }
}
```

### CommunityToolkit.Mvvm (recommended)

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = string.Empty;

    [RelayCommand]
    private async Task LoadDataAsync() { /* ... */ }
}
```

The source generator creates the `Title` property, `PropertyChanged` raise,
and `LoadDataCommand` automatically.

---

## Value Converters — IValueConverter

Implement `Convert` (source → target) and `ConvertBack` (target → source):

```csharp
public class IntToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType,
        object? parameter, CultureInfo culture)
        => value is int i && i != 0;

    public object? ConvertBack(object? value, Type targetType,
        object? parameter, CultureInfo culture)
        => value is true ? 1 : 0;
}
```

Declare in XAML resources and consume:

```xml
<ContentPage.Resources>
    <local:IntToBoolConverter x:Key="IntToBool" />
</ContentPage.Resources>

<Switch IsToggled="{Binding Count, Converter={StaticResource IntToBool}}" />
```

`ConverterParameter` is always passed as a **string** — parse inside `Convert`:

```xml
<Label Text="{Binding Score, Converter={StaticResource ThresholdConverter},
              ConverterParameter=50}" />
```

---

## Multi-Binding

Combine multiple source values with `IMultiValueConverter`:

```xml
<Label>
    <Label.Text>
        <MultiBinding Converter="{StaticResource FullNameConverter}">
            <Binding Path="FirstName" />
            <Binding Path="LastName" />
        </MultiBinding>
    </Label.Text>
</Label>
```

```csharp
public class FullNameConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType,
        object parameter, CultureInfo culture)
    {
        if (values.Length == 2 && values[0] is string first
            && values[1] is string last)
            return $"{first} {last}";
        return string.Empty;
    }

    public object[] ConvertBack(object value, Type[] targetTypes,
        object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

---

## Relative Bindings

| Source | Syntax | Use case |
|--------|--------|----------|
| Self | `{Binding Source={RelativeSource Self}, Path=WidthRequest}` | Bind to own properties |
| Ancestor | `{Binding BindingContext.Title, Source={RelativeSource AncestorType={x:Type ContentPage}}}` | Reach parent BindingContext |
| TemplatedParent | `{Binding Source={RelativeSource TemplatedParent}, Path=Padding}` | Inside ControlTemplate |

```xml
<!-- Square box: Height = Width -->
<BoxView WidthRequest="100"
         HeightRequest="{Binding Source={RelativeSource Self}, Path=WidthRequest}" />
```

---

## StringFormat

Use `Binding.StringFormat` for simple display formatting without a converter:

```xml
<Label Text="{Binding Price, StringFormat='Total: {0:C2}'}" />
<Label Text="{Binding DueDate, StringFormat='{0:MMM dd, yyyy}'}" />
```

Wrap the format string in single quotes when it contains commas or braces.

---

## Binding Fallbacks

- **FallbackValue** — used when the binding path cannot be resolved or the
  converter throws.
- **TargetNullValue** — used when the bound value is `null`.

```xml
<Label Text="{Binding MiddleName, TargetNullValue='(none)',
              FallbackValue='unavailable'}" />
<Image Source="{Binding AvatarUrl, TargetNullValue='default_avatar.png'}" />
```

---

## .NET 9+ Code Bindings (AOT-safe)

Fully AOT-safe, no reflection:

```csharp
label.SetBinding(Label.TextProperty,
    static (PersonViewModel vm) => vm.FullName);

entry.SetBinding(Entry.TextProperty,
    static (PersonViewModel vm) => vm.Age,
    mode: BindingMode.TwoWay,
    converter: new IntToStringConverter());
```

---

## Threading

MAUI automatically marshals `PropertyChanged` to the UI thread — you can raise
it from any thread. **However**, direct `ObservableCollection` mutations
(Add / Remove) from background threads may crash:

```csharp
// ✅ Safe — PropertyChanged is auto-marshalled
await Task.Run(() => Title = "Loaded");

// ⚠️ ObservableCollection.Add — dispatch to UI thread
MainThread.BeginInvokeOnMainThread(() => Items.Add(newItem));
```

---

## Common Pitfalls

| Mistake | Fix |
|---------|-----|
| Missing `x:DataType` — bindings silently fall back to reflection | Add `x:DataType` at page root and every `DataTemplate`; enable `XC0025` as error |
| Forgetting to set `BindingContext` | Set in XAML (`<Page.BindingContext>`) or inject via constructor |
| Specifying redundant `Mode=OneWay` / `Mode=TwoWay` | Omit `Mode` when using the control's default |
| ViewModel does not implement `INotifyPropertyChanged` | Use `ObservableObject` from CommunityToolkit.Mvvm or implement manually |
| Mutating `ObservableCollection` off the UI thread | Wrap mutations in `MainThread.BeginInvokeOnMainThread` |
| Complex converter chains in hot paths | Pre-compute values in the ViewModel instead |
| Using `x:DataType="x:Object"` to escape compiled bindings | Restructure bindings; keep compile-time safety |
| Binding to non-public properties | Binding targets must be `public` properties (fields are ignored) |

---

## References

- [Data binding overview](https://learn.microsoft.com/dotnet/maui/fundamentals/data-binding/)
- [Compiled bindings](https://learn.microsoft.com/dotnet/maui/fundamentals/data-binding/compiled-bindings)
- [Value converters](https://learn.microsoft.com/dotnet/maui/fundamentals/data-binding/converters)
- [Relative bindings](https://learn.microsoft.com/dotnet/maui/fundamentals/data-binding/relative-bindings)
- [Multi-bindings](https://learn.microsoft.com/dotnet/maui/fundamentals/data-binding/multibindings)
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/)
