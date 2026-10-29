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
            Value = nextValue;
        }

        await ValueChanged.InvokeAsync(nextValue);
    }
}
