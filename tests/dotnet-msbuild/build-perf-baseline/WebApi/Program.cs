using BusinessLogic;

namespace WebApi;

public class Program
{
    public static void Main(string[] args)
    {
        var service = new EntityService();

        var result = service.CreateEntity("Test Entity");
        if (result.Success)
            Console.WriteLine($"Created: {result.Data!.Name} (ID: {result.Data.Id})");

        var all = service.ListEntities();
        Console.WriteLine($"Total entities: {all.Count}");
    }
}
