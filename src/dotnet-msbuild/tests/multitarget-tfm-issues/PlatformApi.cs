using System;

namespace MultiTargetLib;

public class PlatformApi
{
    public string GetInfo()
    {
#if NET8_0_OR_GREATER
        // This API is only available in .NET 6+
        return $"PID: {Environment.ProcessId}";
#elif NETSTANDARD2_0
        // netstandard2.0 doesn't have Environment.ProcessId
        return $"PID: {System.Diagnostics.Process.GetCurrentProcess().Id}";
#elif NETFRAMEWORK
        return $"PID: {System.Diagnostics.Process.GetCurrentProcess().Id} (Framework)";
#endif
    }

    public void ProcessData(ReadOnlySpan<byte> data)
    {
        Console.WriteLine($"Processing {data.Length} bytes");
    }
}
