namespace SourceDiscovery;

public partial class SelectionToggle
{
    private async Task HandleToggleAsync()
    {
        if (!AllowChange)
        {
            return;
        }

        var nextValue = !CurrentValue;
        if (nextValue != Value)
        {
            CurrentValue = nextValue;
        }

        await ValueChanged.InvokeAsync(nextValue);
    }
}
