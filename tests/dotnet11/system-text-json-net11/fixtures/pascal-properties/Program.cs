using System.Text.Json;

var options = new JsonSerializerOptions
{
    PropertyNamingPolicy = new PascalCasePolicy()
};

Console.WriteLine(JsonSerializer.Serialize(new Person("Jane", 30), options));

record Person(string name, int age);

sealed class PascalCasePolicy : JsonNamingPolicy
{
    public override string ConvertName(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToUpperInvariant(name[0]) + name[1..];
}
