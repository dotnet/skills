using System.Text.Json;
using System.Text.Json.Serialization;

public sealed record Message(string Text, int Priority);

[JsonSerializable(typeof(Message))]
public partial class AppJsonContext : JsonSerializerContext
{
}

public static class MessageSerializer
{
    public static string Serialize(Message message)
        => JsonSerializer.Serialize(message, AppJsonContext.Default.Message);
}
