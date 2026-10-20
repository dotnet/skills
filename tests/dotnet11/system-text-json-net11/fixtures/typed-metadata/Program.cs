using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

var options = new JsonSerializerOptions
{
    TypeInfoResolver = new DefaultJsonTypeInfoResolver()
};

JsonTypeInfo<Order> typeInfo =
    (JsonTypeInfo<Order>)options.GetTypeInfo(typeof(Order));

Console.WriteLine(JsonSerializer.Serialize(new Order(42, 19.95m), typeInfo));

record Order(int Id, decimal Total);
