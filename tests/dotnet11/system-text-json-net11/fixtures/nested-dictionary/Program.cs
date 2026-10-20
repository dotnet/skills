using System.Text.Json;

var options = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.PascalCase
};

var dashboard = new Dashboard(new Dictionary<string, int>
{
    ["pendingOrders"] = 2
});

Console.WriteLine(JsonSerializer.Serialize(dashboard, options));

record Dashboard(Dictionary<string, int> stats);
