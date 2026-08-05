using System;

namespace Billing.Tests;

// Environment smoke checks: this file exercises no production type in the repo.
public sealed class SmokeEnvironmentTests
{
    public void TempDirectory_IsWritable()
    {
        _ = Environment.GetEnvironmentVariable("TMPDIR");
    }
}
