namespace LifecycleSample;

public sealed class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new DraftWindow(new AppShell());
        window.Created += (_, _) => LifecycleLog.Write("Created");
        window.Activated += (_, _) => LifecycleLog.Write("Activated");
        return window;
    }
}

public sealed class DraftWindow : Window
{
    public DraftWindow(Page page) : base(page)
    {
    }

    protected override void OnStopped()
    {
        base.OnStopped();
        DraftState.Save();
    }

    protected override void OnResumed()
    {
        base.OnResumed();
        DraftState.Restore();
    }
}
