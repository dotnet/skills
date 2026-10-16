using System.Text.Json;

public sealed record Message(string Text, int Priority);

public static class MessageSerializer
{
    public static string Serialize(Message message)
        => JsonSerializer.Serialize((object)message, typeof(Message), new JsonSerializerOptions());
}
