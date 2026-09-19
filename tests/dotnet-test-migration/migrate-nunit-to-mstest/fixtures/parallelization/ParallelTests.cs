using NUnit.Framework;

[assembly: Parallelizable(ParallelScope.Fixtures)]
[assembly: LevelOfParallelism(2)]

namespace Sample.Tests;

[TestFixture]
public class FastTests
{
    [Test]
    public void RunsNormally()
    {
        Assert.That(true, Is.True);
    }
}

[TestFixture]
[NonParallelizable]
public class DatabaseTests
{
    [Test]
    public void UsesExclusiveDatabase()
    {
        Assert.That(true, Is.True);
    }
}
