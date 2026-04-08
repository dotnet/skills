using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using UserManagement;

namespace UserManagement.Tests;

[TestClass]
public sealed class AuthenticationServiceTests
{
    private Mock<IUserStore> _mockUserStore = null!;
    private Mock<IPasswordHasher> _mockHasher = null!;
    private Mock<ILogger<AuthenticationService>> _mockLogger = null!;
    private Mock<IOptions<UserManagerOptions>> _mockOptions = null!;
    private AuthenticationService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockUserStore = new Mock<IUserStore>();
        _mockHasher = new Mock<IPasswordHasher>();
        _mockLogger = new Mock<ILogger<AuthenticationService>>();
        _mockOptions = new Mock<IOptions<UserManagerOptions>>();

        _mockOptions.Setup(o => o.Value).Returns(new UserManagerOptions
        {
            MaxLoginAttempts = 5,
            LockoutDuration = TimeSpan.FromMinutes(15)
        });

        _service = new AuthenticationService(
            _mockUserStore.Object,
            _mockHasher.Object,
            _mockLogger.Object,
            _mockOptions.Object);
    }

    [TestMethod]
    public void Login_ValidCredentials_ReturnsTrue()
    {
        var user = new User(1, "alice@example.com", "hashed", 0, null);
        _mockUserStore.Setup(s => s.FindByEmail("alice@example.com")).Returns(user);
        _mockHasher.Setup(h => h.Verify("password123", "hashed")).Returns(true);

        var result = _service.Login("alice@example.com", "password123");

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void Login_InvalidPassword_ReturnsFalse()
    {
        var user = new User(1, "alice@example.com", "hashed", 0, null);
        _mockUserStore.Setup(s => s.FindByEmail("alice@example.com")).Returns(user);
        _mockHasher.Setup(h => h.Verify("wrong", "hashed")).Returns(false);

        var result = _service.Login("alice@example.com", "wrong");

        Assert.IsFalse(result);
        _mockUserStore.Verify(s => s.Update(It.Is<User>(u => u.FailedAttempts == 1)), Times.Once);
    }

    [TestMethod]
    public void Login_UnknownEmail_ReturnsFalse()
    {
        _mockUserStore.Setup(s => s.FindByEmail("unknown@example.com")).Returns((User?)null);

        var result = _service.Login("unknown@example.com", "any");

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void Login_LockedAccount_ReturnsFalse()
    {
        var user = new User(1, "alice@example.com", "hashed", 5, DateTime.UtcNow.AddMinutes(10));
        _mockUserStore.Setup(s => s.FindByEmail("alice@example.com")).Returns(user);
        // These setups are never reached because account is locked
        _mockHasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(true);

        var result = _service.Login("alice@example.com", "password123");

        Assert.IsFalse(result);
    }
}
