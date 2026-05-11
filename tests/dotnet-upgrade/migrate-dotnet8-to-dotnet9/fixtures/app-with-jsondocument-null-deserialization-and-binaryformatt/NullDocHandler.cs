using System.Text.Json;
class NullDocHandler
{
    void Handle()
    {
        var doc = JsonSerializer.Deserialize<JsonDocument>("null");
        if (doc is null)
        {
            Console.WriteLine("Got null document");
            return;
        }
        Console.WriteLine(doc.RootElement);
    }
}
