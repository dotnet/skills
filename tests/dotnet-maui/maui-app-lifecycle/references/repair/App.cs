namespace LifecycleSample;

public sealed class AppWindow : Window
{
    private readonly DraftViewModel _viewModel;

    public AppWindow(Page page, DraftViewModel viewModel) : base(page)
    {
        _viewModel = viewModel;
    }

    protected override void OnStopped()
    {
        base.OnStopped();
        Preferences.Set("draft_text", _viewModel.DraftText);
    }

    protected override void OnResumed()
    {
        base.OnResumed();
        _viewModel.DraftText = Preferences.Get("draft_text", string.Empty);
    }
}
