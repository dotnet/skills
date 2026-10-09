# Specialized binding examples

Read only the section needed for the request. These are binding fragments;
complete pages also need the supplied ViewModel's runtime context, matching
namespace declarations and a single content root.

## Binding modes

Override a property's default only when needed:

| Mode | Direction | Typical use |
|---|---|---|
| OneWay | Source to target | Display |
| TwoWay | Both ways | Editable `Entry.Text` / `Switch.IsToggled` (their defaults) |
| OneWayToSource | Target to source | Input without pushing values back |
| OneTime | Initial transfer | Static values without change tracking |

```xml
<Label Text="{Binding Score}" />
<Entry Text="{Binding UserName}" />
<Label Text="{Binding Title, Mode=OneTime}" />
<Entry Text="{Binding SearchQuery, Mode=OneWayToSource}" />
```

## IValueConverter and resources

Define every converter resource used by the returned markup. The display-only
mapping below uses OneWay so it does not accidentally modify the count:

```csharp
using System;
using System.Globalization;
using Microsoft.Maui.Controls;

namespace MyApp.Converters;

public sealed class IntToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType,
        object parameter, CultureInfo culture) => value is int count && count != 0;

    public object ConvertBack(object value, Type targetType,
        object parameter, CultureInfo culture) => value is true ? 1 : 0;
}
```

For an enum-to-color request, map each supplied enum member to a `Color` and
include an explicit neutral fallback. For a display-only converter, an
unsupported reverse conversion can return `BindableProperty.UnsetValue`.
Do not reference a converter or model namespace that was never declared.

```xml
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:conv="clr-namespace:MyApp.Converters"
             xmlns:vm="clr-namespace:MyApp.ViewModels"
             x:DataType="vm:MainViewModel">
    <ContentPage.Resources>
        <conv:IntToBoolConverter x:Key="IntToBool" />
    </ContentPage.Resources>
    <Switch IsToggled="{Binding Count, Mode=OneWay,
                       Converter={StaticResource IntToBool}}" />
</ContentPage>
```

This page assumes an existing `MainViewModel.Count` and runtime context. Literal
`ConverterParameter=50` is supplied as a string; parse it for a numeric threshold.
Do not assume every parameter is a string when the caller supplies an object
through a resource or another markup extension.

## Multi-binding

Declare the `FullNameConverter` resource before using it:

```xml
<Label>
    <Label.Text>
        <MultiBinding Mode="OneWay" Converter="{StaticResource FullNameConverter}">
            <Binding Path="FirstName" />
            <Binding Path="LastName" />
        </MultiBinding>
    </Label.Text>
</Label>
```

```csharp
using System;
using System.Globalization;
using Microsoft.Maui.Controls;

namespace MyApp.Converters;

public sealed class FullNameConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType,
        object parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] is string first && values[1] is string last
            ? $"{first} {last}"
            : BindableProperty.UnsetValue;

    public object[] ConvertBack(object value, Type[] targetTypes,
        object parameter, CultureInfo culture) => throw new NotSupportedException();
}
```

The explicit OneWay contract does not call `ConvertBack`. A requested TwoWay
mapping needs a real reverse implementation, not this display-only converter.

## Relative bindings

| Source | Fragment | Purpose |
|---|---|---|
| Self | `{Binding Source={RelativeSource Self}, Path=WidthRequest}` | Own property |
| Ancestor | `{Binding BindingContext.Title, Source={RelativeSource AncestorType={x:Type ContentPage}}}` | Parent context |
| TemplatedParent | `{Binding Source={RelativeSource TemplatedParent}, Path=Padding}` | Control template |

```xml
<BoxView WidthRequest="100"
         HeightRequest="{Binding Source={RelativeSource Self}, Path=WidthRequest}" />
```

With source compilation, type the actual source/path appropriately rather than
assuming the page ViewModel. Behaviors do not inherit a view's binding context.

## Formatting and fallbacks

Use `StringFormat` for simple formatting instead of adding a converter:

```xml
<Label Text="{Binding Price, StringFormat='Total: {0:C2}'}" />
<Label Text="{Binding DueDate, StringFormat='{0:MMM dd, yyyy}'}" />
<Label Text="{Binding MiddleName, TargetNullValue='(none)',
              FallbackValue='unavailable'}" />
```

`FallbackValue` handles an unresolved source/path. `TargetNullValue` handles a
resolved null result. They are not a general exception handler for converters;
do not claim a thrown converter exception is safely replaced by `FallbackValue`.

## Typed C# bindings (.NET 9+)

Use the typed lambda overload for an AOT-safe, reflection-free code binding:

```csharp
label.SetBinding(Label.TextProperty,
    static (PersonViewModel vm) => vm.FullName);
```

For conversions or a different mode, pass the appropriate `converter` / `mode`
arguments and provide the converter implementation. Typed binding metadata
still does not create the runtime context or property-change notifications.

## References

- [Data binding overview](https://learn.microsoft.com/dotnet/maui/fundamentals/data-binding/)
- [Value converters](https://learn.microsoft.com/dotnet/maui/fundamentals/data-binding/converters)
- [Relative bindings](https://learn.microsoft.com/dotnet/maui/fundamentals/data-binding/relative-bindings)
- [Multi-bindings](https://learn.microsoft.com/dotnet/maui/fundamentals/data-binding/multibindings)
- [Binding fallbacks](https://learn.microsoft.com/dotnet/maui/fundamentals/data-binding/binding-fallbacks)
