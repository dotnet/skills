using NUnit.Framework;

namespace Sample.Tests;

[TestFixture]
public class ThreadingTests
{
    [Test]
    [CancelAfter(5_000)]
    public async Task UsesCooperativeCancellation(
        CancellationToken cancellationToken)
    {
        await Task.Delay(1, cancellationToken);
        Assert.That(cancellationToken.CanBeCanceled, Is.True);
    }
}
