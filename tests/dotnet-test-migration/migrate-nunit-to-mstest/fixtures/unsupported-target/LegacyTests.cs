using NUnit.Framework;

namespace Sample.Tests;

[TestFixture]
public class LegacyTests
{
    [Test]
    public void Passes()
    {
        Assert.That(true, Is.True);
    }
}
