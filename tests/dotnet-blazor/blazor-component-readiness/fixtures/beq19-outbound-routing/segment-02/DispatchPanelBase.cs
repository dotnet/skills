using Microsoft.AspNetCore.Components;

namespace DockDispatch;

public class DispatchPanelBase : ComponentBase
{
    [Parameter] public string DockId { get; set; } = "dock-4";
    [Parameter] public string ReservationCode { get; set; } = "batch-42";
    [Parameter] public string[] Labels { get; set; } = [];
    [Parameter] public bool QueueEnabled { get; set; }

    private readonly DeliveryChannel channel = new();

    protected async Task PublishAsync()
    {
        var snapshot = new DispatchSnapshot(DockId, Labels);
        await channel.SendAsync(SnapshotCodec.Encode(snapshot));
        if (QueueEnabled)
        {
            var reservation = DispatchPlan.Capture(ReservationCode);
            await Outbox.EnqueueAsync(channel, reservation);
        }
    }
}

public sealed record DispatchSnapshot(string DockId, string[] Labels);
public sealed record Reservation(string Code);

public static class DispatchPlan
{
    public static Reservation Capture(string code) => new(code);
}
