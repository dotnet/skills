using System.Buffers;
using System.Text.Json;

namespace DockDispatch;

public static class EnvelopeCodec
{
    public static byte[] Encode(Reservation reservation)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("operation", "reserve");
            writer.WritePropertyName("reservation");
            writer.WriteStringValue(reservation.Code);
            writer.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }
}
