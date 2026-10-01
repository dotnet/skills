using System.Text.Json;
using System.Text.Json.Serialization;

var options = new JsonSerializerOptions
{
    TypeInfoResolver = MessageContext.Default
};

var known = options.GetTypeInfo<KnownMessage>() is not null;
var unknown = true;
try
{
    _ = options.GetTypeInfo<UnknownMessage>();
}
catch (NotSupportedException)
{
    unknown = false;
}

Console.WriteLine($"Known={known}");
Console.WriteLine($"Unknown={unknown}");

record KnownMessage(string Text);
record UnknownMessage(string Text);

[JsonSerializable(typeof(KnownMessage))]
partial class MessageContext : JsonSerializerContext;
