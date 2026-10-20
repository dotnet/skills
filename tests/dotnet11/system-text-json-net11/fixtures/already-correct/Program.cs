using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

var options = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.PascalCase,
    DictionaryKeyPolicy = JsonNamingPolicy.PascalCase,
    TypeInfoResolver = new DefaultJsonTypeInfoResolver()
};

JsonTypeInfo<Person> typeInfo = options.GetTypeInfo<Person>();
var person = new Person("Jane", 30, new Dictionary<string, int>
{
    ["pendingOrders"] = 2
});

Console.WriteLine(JsonSerializer.Serialize(person, typeInfo));

record Person(string name, int age, Dictionary<string, int> stats);
