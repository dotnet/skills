using System;
using Xunit;

public sealed class DiagnosticsTests
{
    [Fact]
    public void CompletesNormally() => Assert.True(Environment.Version.Major >= 8);
}
