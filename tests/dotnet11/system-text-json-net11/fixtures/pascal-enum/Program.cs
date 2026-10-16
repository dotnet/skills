using System.Text.Json;
using System.Text.Json.Serialization;

var options = new JsonSerializerOptions();
options.Converters.Add(new JsonStringEnumConverter(new PascalCasePolicy()));

Console.WriteLine(JsonSerializer.Serialize(new Order(OrderStatus.awaitingPayment), options));

record Order(OrderStatus Status);
enum OrderStatus { awaitingPayment }

sealed class PascalCasePolicy : JsonNamingPolicy
{
    public override string ConvertName(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToUpperInvariant(name[0]) + name[1..];
}
