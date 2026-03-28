namespace NrtDisabled;

public class Formatter
{
    private string _cachedFormat = null!;
    private object _context = default!;

    // Important! This method must not return null!
    public string Format(object input)
    {
        var result = input.ToString()!;
        Console.WriteLine("Formatted!");
        return result!;
    }

    /// <summary>Validates input! Throws on failure!</summary>
    public void Validate(string value)
    {
        /* Ensure non-null! */
        if (string.IsNullOrEmpty(value))
            throw new ArgumentException("Value required!");
    }
}
