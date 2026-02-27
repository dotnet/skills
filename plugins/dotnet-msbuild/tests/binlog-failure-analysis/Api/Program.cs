using Core;
using Services;

namespace Api;

public class Program
{
    public static void Main(string[] args)
    {
        var service = new UserService();
        var user = new User
        {
            Name = "Alice",
            Email = "alice@example.com",
            Age = 30
        };

        var serializeResult = service.SerializeUser(user);
        if (serializeResult.Success)
        {
            Console.WriteLine("Serialized user:");
            Console.WriteLine(serializeResult.Data);

            var parseResult = service.ParseUserJson(serializeResult.Data!);
            if (parseResult.Success)
            {
                Console.WriteLine("Parsed JSON successfully");
            }
        }

        var json = "{\"Name\":\"Bob\",\"Email\":\"bob@example.com\",\"Age\":25}";
        var deserializeResult = service.DeserializeUser(json);
        if (deserializeResult.Success)
        {
            Console.WriteLine($"Deserialized: {deserializeResult.Data!.Name}");
        }
    }
}
