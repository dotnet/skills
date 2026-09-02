using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MyApp.Tests;

public interface IAuditable { }

public interface IPrincipal { }

public sealed class User : IAuditable, IPrincipal
{
    public string Name { get; set; } = "";
}

[TestClass]
public class UserServiceTests
{
    [TestMethod]
    public void GetUser_BothViewsOfSameUser_AreEqual()
    {
        var user = new User { Name = "Alice" };
        IAuditable auditable = user;
        IPrincipal principal = user;
        Assert.AreEqual(auditable, principal);
    }

    [TestMethod]
    public void GetUser_SameReference_AreSame()
    {
        var user = new User { Name = "Bob" };
        IAuditable auditable = user;
        IPrincipal principal = user;
        Assert.AreSame(auditable, principal);
    }

    [TestMethod]
    [DataRow(1L, "Alice")]
    [DataRow(2L, "Bob")]
    public void GetUserName_ReturnsCorrectName(int id, string expectedName)
    {
        Assert.IsNotNull(expectedName);
    }

    [TestMethod]
    [Timeout(3000)]
    public void ProcessUser_CompletesInTime()
    {
        Assert.IsTrue(true);
    }
}
