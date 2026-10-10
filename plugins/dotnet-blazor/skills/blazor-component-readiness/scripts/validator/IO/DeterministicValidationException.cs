namespace BlazorComponentReadiness.Validator.IO;

public class DeterministicValidationException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class ResourceLimitException(string resource, long maximum, long actual)
    : DeterministicValidationException(
        $"{resource} exceeds the {maximum}-byte limit; observed {actual} bytes.")
{
    public string Resource { get; } = resource;

    public long Maximum { get; } = maximum;

    public long Actual { get; } = actual;
}
