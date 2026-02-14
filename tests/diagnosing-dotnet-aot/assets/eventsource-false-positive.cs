// Test asset: EventSource with >3 params triggers IL2026, but is safe for primitives
// Expected: Skill should identify this as a known false positive safe to suppress

using System.Diagnostics.Tracing;

namespace MyApp;

[EventSource(Name = "MyApp-Operations")]
public sealed class AppEventSource : EventSource
{
    public static readonly AppEventSource Log = new();

    // IL2026 warning on WriteEvent because >3 params — but safe for primitive types
    [Event(1, Level = EventLevel.Informational)]
    public void OperationCompleted(string operationName, int operationId, long durationMs, string status)
    {
        WriteEvent(1, operationName, operationId, durationMs, status);
    }

    // Also safe — all primitives
    [Event(2, Level = EventLevel.Warning)]
    public void OperationFailed(string operationName, int operationId, long durationMs, string errorCode, int retryCount)
    {
        WriteEvent(2, operationName, operationId, durationMs, errorCode, retryCount);
    }
}
