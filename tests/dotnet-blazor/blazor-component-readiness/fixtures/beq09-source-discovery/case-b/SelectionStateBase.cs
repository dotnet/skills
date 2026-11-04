using Microsoft.AspNetCore.Components;

namespace SourceDiscovery;

public abstract class SelectionStateBase : ComponentBase
{
    [Parameter]
    public bool Value { get; set; }

    [Parameter]
    public bool AllowChange { get; set; }

    [Parameter]
    public EventCallback<bool> ValueChanged { get; set; }

    protected bool CurrentValue;

    protected override void OnParametersSet()
    {
        CurrentValue = Value;
    }
}
