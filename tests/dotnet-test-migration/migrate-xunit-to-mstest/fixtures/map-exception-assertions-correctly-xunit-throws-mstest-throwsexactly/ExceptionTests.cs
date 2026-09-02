using System;
using System.Threading.Tasks;
using Xunit;

namespace MyApp.Tests;

public class ExceptionTests
{
    [Fact]
    public void Throws_ExactType()
    {
        Assert.Throws<InvalidOperationException>(() => throw new InvalidOperationException("x"));
    }

    [Fact]
    public void ThrowsAny_AcceptsDerived()
    {
        Assert.ThrowsAny<Exception>(() => throw new InvalidOperationException("x"));
    }

    [Fact]
    public async Task ThrowsAsync_ExactType()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => { await Task.Yield(); throw new InvalidOperationException(); });
    }
}
