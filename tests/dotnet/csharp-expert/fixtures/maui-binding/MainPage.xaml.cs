namespace MauiFixture;

public partial class MainPage : ContentPage
{
    public StatusViewModel ViewModel { get; } = new();

    public MainPage()
    {
        InitializeComponent();
        BindingContext = ViewModel;
    }

    private void OnAdvanceClicked(object? sender, EventArgs e)
    {
        ViewModel.Status = "Ready";
    }
}
