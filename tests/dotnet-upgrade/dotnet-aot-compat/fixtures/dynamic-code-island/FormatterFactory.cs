using System.Diagnostics.CodeAnalysis;

public interface IValueFormatter
{
    string Format(int value);
}

public sealed class StaticFormatter : IValueFormatter
{
    public string Format(int value) => $"value:{value}";
}

public sealed class EmitFormatter : IValueFormatter
{
    [RequiresDynamicCode("Builds a formatter with Reflection.Emit.")]
    public EmitFormatter()
    {
    }

    public string Format(int value) => $"value:{value}";
}

public static class FormatterFactory
{
    public static IValueFormatter Create() => new EmitFormatter();
}
