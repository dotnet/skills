namespace DockDispatch;

public static class Outbox
{
    public static Task EnqueueAsync(DeliveryChannel channel, Reservation reservation)
    {
        var payload = EnvelopeCodec.Encode(reservation);
        return channel.SendAsync(payload);
    }
}

public sealed class DeliveryChannel
{
    public ReadOnlyMemory<byte> LastSent { get; private set; }

    public Task SendAsync(ReadOnlyMemory<byte> payload)
    {
        LastSent = payload.ToArray();
        return Task.CompletedTask;
    }
}
