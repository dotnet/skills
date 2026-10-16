using System.Text.Json;

var options = new JsonSerializerOptions();
var available = true;
try
{
    _ = options.GetTypeInfo<Person>();
}
catch (NotSupportedException)
{
    available = false;
}

Console.WriteLine($"Available={available}");

record Person(string Name);
