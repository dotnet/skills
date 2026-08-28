using System.Diagnostics;

namespace NeedsWrappers.Services;

public sealed class ProcessLauncher
{
    public int Run(string fileName, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException("Process failed to start.");

        process.WaitForExit();
        return process.ExitCode;
    }
}
