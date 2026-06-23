using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MyApp.Tests;

public class CategorizedTestMethodAttribute : TestMethodAttribute
{
    public string Category { get; }

    public CategorizedTestMethodAttribute(string category)
    {
        Category = category;
    }
}

[TestClass]
public class ServiceTests
{
    [CategorizedTestMethod("Integration")]
    public void ConnectToDatabase_Succeeds()
    {
        Assert.IsTrue(true);
    }

    [TestMethod("My custom display name")]
    public void SimpleTest()
    {
        Assert.IsTrue(true);
    }
}
