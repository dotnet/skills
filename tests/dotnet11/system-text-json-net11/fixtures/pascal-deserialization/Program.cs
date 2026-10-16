using System.Text.Json;
using System.Text.Json.Serialization;

const string json = """{"Name":"Jane","Age":30}""";
var person = JsonSerializer.Deserialize<Person>(json);
Console.WriteLine($"{person!.name}|{person.age}");

record Person(
    [property: JsonPropertyName("Name")] string name,
    [property: JsonPropertyName("Age")] int age);
