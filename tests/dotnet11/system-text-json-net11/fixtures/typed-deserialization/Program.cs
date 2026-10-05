using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

const string json = """{"Id":42,"Total":19.95}""";
var options = new JsonSerializerOptions
{
    TypeInfoResolver = new DefaultJsonTypeInfoResolver()
};

JsonTypeInfo<Order> typeInfo =
    (JsonTypeInfo<Order>)options.GetTypeInfo(typeof(Order));
var order = JsonSerializer.Deserialize(json, typeInfo)!;

Console.WriteLine($"{order.Id}|{order.Total}");

record Order(int Id, decimal Total);
