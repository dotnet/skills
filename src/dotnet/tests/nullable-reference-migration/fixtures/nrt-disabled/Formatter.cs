namespace NrtDisabled;

public class Formatter
{
    public string Format(object input)
    {
        var result = input.ToString()!;
        return result!;
    }
}
