# Page-owned theme event subscriptions

`Application.RequestedThemeChanged` is an instance event implemented through
MAUI 10's weak-event manager, not proof of a strong-reference leak. Capture that publisher, subscribe/unsubscribe the
same handler, and support repeated appearances. This programmatic page example
does not require generated XAML methods:

```csharp
using Microsoft.Maui.Controls;

public class ThemeAwarePage : ContentPage
{
    private readonly Application _publisher;
    private bool _observing;

    public ThemeAwarePage(Application publisher) => _publisher = publisher;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (!_observing)
        {
            _publisher.RequestedThemeChanged += OnThemeChanged;
            _observing = true;
        }
    }

    protected override void OnDisappearing()
    {
        if (_observing)
        {
            _publisher.RequestedThemeChanged -= OnThemeChanged;
            _observing = false;
        }
        base.OnDisappearing();
    }

    protected virtual void OnThemeChanged(object? sender, AppThemeChangedEventArgs e)
        => System.Diagnostics.Debug.WriteLine(e.RequestedTheme);
}
```

Retain the rest of an existing page rather than replacing its UI with this
example. Refresh any page-local effective theme on appearance if changes while
it was hidden matter to its state.

Do not subscribe only during construction and unsubscribe on disappearance;
that pair fails when the same page returns. Do not declare `IDisposable` without
implementing `Dispose`, and do not assume MAUI automatically disposes pages.

Source: [MAUI 10 Application event implementation](https://github.com/dotnet/maui/blob/10.0.0/src/Controls/src/Core/Application/Application.cs).
