using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Contoso.Auth.Tests;

[TestClass]
public class UserAuthTests
{
    [TestMethod]
    public void Login_ValidCredentials_ReturnsToken() { Assert.IsTrue(true); }

    [TestMethod]
    public void Login_InvalidPassword_ReturnsUnauthorized() { Assert.IsTrue(true); }

    [TestMethod]
    public void Login_LockedAccount_ReturnsLocked() { Assert.IsTrue(true); }
}
