using System.Text.Json;

namespace Contoso.Services;

public class DataService
{
    private readonly HttpClient _client;

    public DataService()
    {
        _client = new HttpClient(); // new HttpClient per instance
    }

    /// <summary>
    /// Fetches and deserializes JSON data from multiple endpoints.
    /// </summary>
    public async Task<List<Record>> FetchAllRecordsAsync(string[] endpoints)
    {
        var results = new List<Record>();

        foreach (var endpoint in endpoints)
        {
            // Sequential instead of concurrent
            var response = await _client.GetAsync(endpoint);
            var json = await response.Content.ReadAsStringAsync();
            var records = JsonSerializer.Deserialize<List<Record>>(json);
            results.AddRange(records);
        }

        return results;
    }

    /// <summary>
    /// Reads a large file and processes each line.
    /// </summary>
    public async Task<int> CountMatchingLinesAsync(string filePath, string searchTerm)
    {
        var content = await File.ReadAllTextAsync(filePath); // reads entire file into memory
        var lines = content.Split('\n');
        var count = 0;

        foreach (var line in lines)
        {
            if (line.ToLower().Contains(searchTerm.ToLower())) // allocates on every iteration
                count++;
        }

        return count;
    }
}

public class Record
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Value { get; set; }
}
