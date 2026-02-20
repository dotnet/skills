using System.Text.RegularExpressions;

namespace Contoso.Processing;

public class TextProcessor
{
    private static readonly HttpClient _client = new HttpClient();

    /// <summary>
    /// Downloads content and processes it line-by-line.
    /// </summary>
    public async Task<string> ProcessUrlAsync(string url)
    {
        var content = await _client.GetStringAsync(url).Result; // sync-over-async
        var lines = content.Split('\n');
        var result = "";

        foreach (var line in lines)
        {
            result += await TransformLineAsync(line); // string concat in loop
        }

        return result;
    }

    private async Task<string> TransformLineAsync(string line)
    {
        await Task.Yield();
        return line.ToLower().Trim();
    }

    /// <summary>
    /// Validates email addresses from user input.
    /// </summary>
    public bool IsValidEmail(string input)
    {
        // Catastrophic backtracking risk (ReDoS)
        var pattern = @"^([a-zA-Z0-9]+)*@[a-zA-Z0-9]+\.[a-zA-Z]+$";
        return Regex.IsMatch(input, pattern);
    }
}
