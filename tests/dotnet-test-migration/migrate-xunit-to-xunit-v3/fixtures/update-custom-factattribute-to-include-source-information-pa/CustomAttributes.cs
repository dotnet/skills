using Xunit;

namespace MyApp.Tests;

internal sealed class RetryFactAttribute : FactAttribute
{
    public int MaxRetries { get; set; } = 3;

    public RetryFactAttribute()
    {
    }
}

internal sealed class ConditionalTheoryAttribute : TheoryAttribute
{
    public string Feature { get; set; } = "";

    public ConditionalTheoryAttribute()
    {
    }
}
