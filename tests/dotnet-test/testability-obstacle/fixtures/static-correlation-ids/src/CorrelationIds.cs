namespace Correlation;

public static class CorrelationIds
{
    public static string Create(string prefix) => $"{prefix}-{Guid.NewGuid():N}";
}
