using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

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
    public static IValueFormatter Create()
        => RuntimeFeature.IsDynamicCodeSupported
            ? new EmitFormatter()
            : new StaticFormatter();
}
