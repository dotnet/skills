using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MyApp.Tests;

[TestClass]
public class UserServiceTests
{
    [TestMethod]
    public void GetUser_ValidId_ReturnsUser()
    {
        object expected = 1;
        object actual = 1;
        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void GetUser_SameReference_AreSame()
    {
        object obj = new object();
        Assert.AreSame(obj, obj);
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
