# Data Binding API Reference

## Binding Modes

| Mode | Direction | Use case |
|------|-----------|----------|
| `OneWay` | Source → Target | Display-only (default for most properties) |
| `TwoWay` | Source ↔ Target | Editable controls (`Entry.Text`, `Switch.IsToggled`) |
| `OneWayToSource` | Target → Source | Read user input without pushing back to UI |
| `OneTime` | Source → Target (once) | Static values; no change tracking overhead |

## BindingContext and Property Paths

- Every `BindableObject` inherits `BindingContext` from its parent unless explicitly set.
- Property paths support dot notation and indexers:

```xml
<Label Text="{Binding Address.City}" />
<Label Text="{Binding Items[0].Name}" />
```

- Set `BindingContext` in XAML or code-behind:

```xml
<ContentPage xmlns:vm="clr-namespace:MyApp.ViewModels"
             x:DataType="vm:MainViewModel">
    <ContentPage.BindingContext>
        <vm:MainViewModel />
    </ContentPage.BindingContext>
</ContentPage>
```

## IValueConverter

Implement `IValueConverter` with `Convert` (source → target) and `ConvertBack` (target → source):

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

### Declaring converters in XAML resources

```xml
<ContentPage.Resources>
    <local:IntToBoolConverter x:Key="IntToBool" />
</ContentPage.Resources>

<Switch IsToggled="{Binding Count, Converter={StaticResource IntToBool}}" />
```

### ConverterParameter

`ConverterParameter` is always passed as a **string**. Parse it inside `Convert`:

```xml
<Label Text="{Binding Score, Converter={StaticResource ThresholdConverter},
              ConverterParameter=50}" />
```

```csharp
int threshold = int.Parse((string)parameter);
```

## StringFormat

Use `Binding.StringFormat` for simple display formatting without a converter:

```xml
<Label Text="{Binding Price, StringFormat='Total: {0:C2}'}" />
<Label Text="{Binding DueDate, StringFormat='{0:MMM dd, yyyy}'}" />
```

> **Note:** Wrap the format string in single quotes when it contains commas or braces.

## Multi-Binding

Combine multiple source values with `IMultiValueConverter`:

```xml
<Label>
    <Label.Text>
        <MultiBinding Converter="{StaticResource FullNameConverter}"
                      StringFormat="{}{0}">
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
        if (values.Length == 2 && values[0] is string first && values[1] is string last)
            return $"{first} {last}";
        return string.Empty;
    }

    public object[] ConvertBack(object value, Type[] targetTypes,
        object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

## Relative Bindings

| Source | Syntax | Use case |
|--------|--------|----------|
| Self | `{Binding Source={RelativeSource Self}, Path=Width}` | Bind to own properties |
| Ancestor | `{Binding Source={RelativeSource AncestorType={x:Type vm:ParentVM}}, Path=Title}` | Reach parent BindingContext |
| TemplatedParent | `{Binding Source={RelativeSource TemplatedParent}, Path=Padding}` | Inside ControlTemplate |

```xml
<!-- Square box: Height = Width -->
<BoxView WidthRequest="100"
         HeightRequest="{Binding Source={RelativeSource Self}, Path=WidthRequest}" />
```

## Binding Fallbacks

- **FallbackValue** – used when the binding path cannot be resolved or the converter throws.
- **TargetNullValue** – used when the bound value is `null`.

```xml
<Label Text="{Binding MiddleName, TargetNullValue='(none)',
              FallbackValue='unavailable'}" />
<Image Source="{Binding AvatarUrl, TargetNullValue='default_avatar.png'}" />
```
