using NUnit.Framework;

public static class SharedState
{
    public static bool Ready { get; set; }
}

[SetUpFixture]
public class AssemblySetup
{
    [OneTimeSetUp]
    public void BeforeAssembly()
    {
        SharedState.Ready = true;
    }

    [OneTimeTearDown]
    public void AfterAssembly()
    {
        SharedState.Ready = false;
    }
}

namespace Sample.Tests
{
    [TestFixture]
    public class AssemblySetupTests
    {
        [Test]
        public void SetupRanBeforeTests()
        {
            Assert.That(SharedState.Ready, Is.True);
        }
    }
}
