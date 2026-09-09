namespace Billing.Abstractions;

/// <summary>
/// Ambient clock seam. Registered as a singleton in the composition root.
/// </summary>
public interface IClock
{
    DateTime UtcNow { get; }
}
