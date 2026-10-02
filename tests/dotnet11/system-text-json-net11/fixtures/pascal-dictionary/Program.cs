using System.Text.Json;

var values = new Dictionary<string, int>
{
    ["pendingOrders"] = 2,
    ["activeUsers"] = 5
};

var transformed = values.ToDictionary(
    pair => char.ToUpperInvariant(pair.Key[0]) + pair.Key[1..],
    pair => pair.Value);

Console.WriteLine(JsonSerializer.Serialize(transformed));
