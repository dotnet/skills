---
name: maui-data-binding
description: >-
  Repair and review .NET MAUI binding contexts and ViewModel PropertyChanged
  notifications; implement compiled XAML/C# bindings, ObservableObject,
  converters, binding modes, multi-binding, relative sources and fallbacks.
  USE FOR: setting up compiled bindings with x:DataType, implementing
  INotifyPropertyChanged or CommunityToolkit ObservableObject, creating
  IValueConverter / IMultiValueConverter, choosing binding modes, configuring
  BindingContext, DataTemplate x:DataType mismatches (including CollectionView),
  relative bindings, binding fallbacks, StringFormat,
  code-behind SetBinding with lambdas, and enforcing XC0022/XC0025 warnings.
  DO NOT USE FOR: CollectionView layout, selection, or paging features (use
  maui-collectionview), Shell navigation data passing (use
  maui-shell-navigation), dependency injection (use maui-dependency-injection),
  or animations triggered by property changes (use .NET MAUI animation APIs).
license: MIT
---

# .NET MAUI Data Binding

Fix the binding's runtime source, compilation scope or change notification.
Preserve supplied models and already-working code; do not replace an existing
`BindingContext`, invent collection properties, or add unrelated converters.

For a small markup defect, return the changed fragment and any new namespace
declaration, not a full page scaffold. For a new implementation, supply the
members, package/usings and runtime wiring needed to use it.

## Choose the correction

| Symptom or request | Correction | Do not |
|---|---|---|
| Blank labels | Check the actual inherited/code-behind `BindingContext` and public property paths; wire a context only if missing | Treat shown XAML as proof no context exists, or assume `x:DataType` creates one |
| A later value does not display | Notify for the public bound property on the same ViewModel instance; publish UI-bound state safely | Assign only a field, notify the wrong member, or replace the context |
| A row member is checked against the page ViewModel | Put the row model's `x:DataType` on the `DataTemplate` | Disable compilation with `x:Object`/`x:Null`, or rewrite the surrounding list |
| A child changes its runtime context | Keep the parent type and declare the child's new type; use the inferred context-setting binding below on MAUI 10 | Assume a new `BindingContext` resets inherited compilation metadata |
| An explicit `Source` is a control, not the page ViewModel | Give that binding the source's type and enable source compilation if necessary | Change the entire page's type |
| Build-time checking | Enable strict diagnostics, type each real scope and preserve existing warning settings | Invent `MauiEnableXamlCompilation`, or treat XC0022 as an invalid-member error |
| A scalar selection property is supplied | Bind that scalar to the selected value; use only an actual supplied collection for `ItemsSource` | Invent a collection or bind the scalar to `ItemsSource` |
| A command behavior cannot resolve its command | Supply its context or an explicit source | Assume behaviors inherit their associated view's context |
| A converter is requested | Implement both interface methods and define the resource/prefix used by the binding | Reference an undefined converter or require Toolkit for a simple manual property |

CollectionView layout/selection/paging belongs to `maui-collectionview`; Shell
parameters to `maui-shell-navigation`; construction/registration to
`maui-dependency-injection`. Do not apply MAUI binding APIs to WPF or other UI
frameworks.

## Compile the actual binding scopes

`x:DataType` is inherited compilation metadata, not a ViewModel instance. Put it
at a page/view root with its actual context, on every `DataTemplate`, and at a
child that changes context. Do not scatter it on children sharing the same type.
Typed missing-member paths are compiler errors; missing metadata can instead
leave a reflection binding.

Smallest row-type correction, with `model` declared on the surrounding page:

```xml
<DataTemplate x:DataType="model:Person">
    <Label Text="{Binding FullName}" />
</DataTemplate>
```

On MAUI 10 XamlC, the child's context-setting binding is compiled against the
parent type; the other child bindings use the child's declared type. Prefer
this inferred form when supported:

```xml
<VerticalStackLayout x:DataType="model:Address"
                     BindingContext="{Binding SelectedAddress}">
    <Label Text="{Binding City}" />
</VerticalStackLayout>
```

Here the surrounding page has `x:DataType="vm:CustomerViewModel"`, whose
`SelectedAddress` is an `Address`. Explicit
`BindingContext="{Binding SelectedAddress, x:DataType={x:Type vm:CustomerViewModel}}"`
is also valid when explicit outer typing is needed; it is not mandatory on that
compiler. Context changes normally rebind descendants.

### Page and control scopes

An explicit source needs its own type, without changing the page's ViewModel.
This page fragment assumes the supplied `MainViewModel` already exposes `Title`
and `Progress` and is assigned as the runtime context:

```xml
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:vm="clr-namespace:MyApp.ViewModels"
             x:DataType="vm:MainViewModel">
    <StackLayout>
        <Label Text="{Binding Title}" />
        <Slider x:Name="progressSlider" Value="{Binding Progress}" />
        <Label Text="{Binding Value, Source={x:Reference progressSlider},
                              x:DataType={x:Type Slider}}" />
    </StackLayout>
</ContentPage>
```

For full page markup, declare matching CLR namespaces, preserve/set the real
runtime context and keep one content root (use a layout for sibling controls).
For example, constructor injection assigns `BindingContext = vm` after
`InitializeComponent`; inline `<ContentPage.BindingContext>` construction is
valid when the ViewModel's constructor permits it.

### Diagnostics

| Code | Meaning | Correction |
|---|---|---|
| XC0022 | Missing type metadata; reflection fallback | Type the actual binding scope |
| XC0023 | Explicit null type | Remove the opt-out and supply the correct type |
| XC0024 | Template inherits an outer type | Type the template against its row |
| XC0025 | Explicit source binding not compiled | Enable source compilation and type its source |

These codes/properties are verified against MAUI 10/11. Check installed targets
for other SDK bands rather than guessing diagnostic meanings.

```xml
<MauiStrictXamlCompilation>true</MauiStrictXamlCompilation>
<MauiEnableXamlCBindingWithSourceCompilation>true</MauiEnableXamlCBindingWithSourceCompilation>
<WarningsAsErrors>$(WarningsAsErrors);XC0022;XC0025</WarningsAsErrors>
```

Strict compilation emits the opt-in warnings in ordinary builds. Source
compilation is otherwise on by default only for AOT/full-trim builds on MAUI
10/11. Promoting XC0025 without enabling source compilation reports source
bindings rather than making them compile.

## Publish changes without unnecessary dependencies

For a minimal notifying-property repair, a manual implementation needs no new
package. Bind the label to `Value` on the existing context; call `PublishAsync`
with each new service result, not just the first:

```csharp
using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;

public sealed class DetailsViewModel : INotifyPropertyChanged
{
    private string _value = string.Empty;

    public string Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _value = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Task PublishAsync(string value) =>
        MainThread.InvokeOnMainThreadAsync(() => Value = value);
}
```

```xml
<Label Text="{Binding Value}" />
```

Keep the typed ViewModel reference owned by the page, not a cast of whatever its
`BindingContext` happens to be later. Do not invent an undeclared `service` field
in a supposedly complete class; use the supplied service or show its injection.
MAUI can dispatch binding updates, but this does not mean every setter or event
subscriber runs on the UI thread. Direct control access and bound
`ObservableCollection` mutations require the UI thread.
`ConfigureAwait(false)` does not move a service onto a worker thread; if it drops
the UI continuation context, marshal all bound publication, including
`IsBusy = false` in `finally`, not just fetched data.

For a requested Toolkit ViewModel, add `CommunityToolkit.Mvvm`, use a `partial`
class deriving from `ObservableObject`, import its ComponentModel/Input
namespaces, and show `[ObservableProperty]` and `[RelayCommand]` members.
Generators create notifying public properties and an async command from an async
method. Async relay commands prevent concurrent executions by default; changes
to external `CanExecute` dependencies need `NotifyCanExecuteChanged` or
`[NotifyCanExecuteChangedFor]`.

## Read specialized examples only when needed

The common fixes above do not require a reference read. For converter
implementation/resource wiring, multi-binding, binding modes, relative sources,
formatting, fallbacks or typed C# bindings, read the relevant section of
[specialized-bindings.md](references/specialized-bindings.md), not every section.
Resolve this path relative to this skill's loaded directory. If a reader fails,
use at most a bounded retry at that same path; never search root/home.

For supplied-code checks, report the actual command/result and preserve passing
source. Platform-neutral contracts do not prove XAML compilation, native
rendering or device behavior.

## References

- [Compiled bindings](https://learn.microsoft.com/dotnet/maui/fundamentals/data-binding/compiled-bindings)
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/)
