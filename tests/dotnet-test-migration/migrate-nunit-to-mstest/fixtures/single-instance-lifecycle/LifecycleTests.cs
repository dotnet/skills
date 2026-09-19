using System.Text;
using NUnit.Framework;

namespace Sample.Tests;

[TestFixture]
public class LifecycleTests
{
    private StringBuilder? _builder;

    [OneTimeSetUp]
    public void CreateSharedState()
    {
        _builder = new StringBuilder("ready");
    }

    [SetUp]
    public void BeforeEach()
    {
        _builder!.Append("|setup");
    }

    [TearDown]
    public void AfterEach()
    {
        _builder!.Append("|cleanup");
    }

    [OneTimeTearDown]
    public void ReleaseSharedState()
    {
        _builder = null;
    }

    [Test]
    public void FirstTestSeesSharedState()
    {
        Assert.That(_builder!.ToString(), Does.Contain("ready|setup"));
    }

    [Test]
    public void SecondTestSeesSharedState()
    {
        Assert.That(_builder!.Length, Is.GreaterThan(0));
    }
}
