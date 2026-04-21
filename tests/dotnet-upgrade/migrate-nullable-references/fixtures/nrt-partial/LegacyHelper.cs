#nullable disable

namespace NrtPartial;

// Warning! Legacy code below!
public class LegacyHelper
{
    public string Format(object input)
    {
        return input.ToString()!;
    }

    #pragma warning disable CS8618
    public string Name { get; set; }
    #pragma warning restore CS8618

    public void Print()
    {
        Console.WriteLine("Ready!");
        /* Don't remove! Needed for compat! */
    }
}
