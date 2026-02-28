using BusinessLogic;
using Common;

namespace Tests;

public class EntityServiceTests
{
    public static bool TestCreateEntity()
    {
        var service = new EntityService();
        var result = service.CreateEntity("Test");
        return result.Success && result.Data!.Name == "Test";
    }

    public static bool TestGetMissing()
    {
        var service = new EntityService();
        var result = service.GetEntity(999);
        return !result.Success;
    }

    public static bool TestTruncation()
    {
        var longName = new string('x', 200);
        return longName.Truncate(10) == "xxxxxxxxxx...";
    }

    public static void Main()
    {
        Console.WriteLine($"CreateEntity: {(TestCreateEntity() ? "PASS" : "FAIL")}");
        Console.WriteLine($"GetMissing: {(TestGetMissing() ? "PASS" : "FAIL")}");
        Console.WriteLine($"Truncation: {(TestTruncation() ? "PASS" : "FAIL")}");
    }
}
