using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

var options = new JsonSerializerOptions
{
    TypeInfoResolver = PayloadContext.Default
};

Console.WriteLine(SerializeIfKnown(new KnownPayload("hello")));
Console.WriteLine(SerializeIfKnown(new UnknownPayload("secret")));

string SerializeIfKnown<T>(T value)
{
    try
    {
        JsonTypeInfo<T> info = options.GetTypeInfo<T>();
        return JsonSerializer.Serialize(value, info);
    }
    catch (NotSupportedException)
    {
        return "SKIPPED";
    }
}

record KnownPayload(string Text);
record UnknownPayload(string Text);

[JsonSerializable(typeof(KnownPayload))]
partial class PayloadContext : JsonSerializerContext;
