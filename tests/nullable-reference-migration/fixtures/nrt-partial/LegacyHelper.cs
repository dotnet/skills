#nullable disable

namespace NrtPartial;

public class LegacyHelper
{
    public string Format(object input)
    {
        return input.ToString()!;
    }

    #pragma warning disable CS8618
    public string Name { get; set; }
    #pragma warning restore CS8618
}
