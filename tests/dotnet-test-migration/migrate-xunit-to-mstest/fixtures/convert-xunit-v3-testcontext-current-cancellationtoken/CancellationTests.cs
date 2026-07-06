using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace MyApp.Tests;

public class CancellationTests
{
    [Fact]
    public async Task RespectsCancellation()
    {
        var ct = TestContext.Current.CancellationToken;
        await Task.Delay(1, ct);
        Assert.False(ct.IsCancellationRequested);
    }
}
