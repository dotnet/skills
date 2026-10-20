using System.Text.Json;

var options = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.PascalCase
};

Console.WriteLine(JsonSerializer.Serialize(new Person("Jane", 30), options));

record Person(string name, int age);
