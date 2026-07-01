using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Auth.Tests;

[TestClass]
public sealed class TokenServiceTests
{
    [TestMethod]
    [DataRow("user@example.com", "admin", DisplayName = "Admin user gets full-access token")]
    [DataRow("viewer@example.com", "viewer", DisplayName = "Viewer gets read-only token")]
    [DataRow("api@example.com", "service", DisplayName = "Service account gets API token")]
    public void GenerateToken_ValidUser_ReturnsTokenWithCorrectRole(string email, string role)
    {
        var service = CreateTokenService();

        var token = service.GenerateToken(email, role);

        Assert.IsNotNull(token);
        Assert.AreEqual(role, token.Role);
        Assert.IsTrue(token.ExpiresAt > DateTime.UtcNow);
    }

    [TestMethod]
    public void GenerateToken_ExpiredCredentials_ThrowsAuthException()
    {
        var service = CreateTokenService(clockOffset: TimeSpan.FromDays(-1));

        Assert.ThrowsException<AuthenticationException>(
            () => service.GenerateToken("user@example.com", "admin"));
    }

    [TestMethod]
    public void RevokeToken_ValidToken_MarksRevoked()
    {
        var service = CreateTokenService();
        var token = service.GenerateToken("user@example.com", "admin");

        service.RevokeToken(token.Id);

        Assert.IsTrue(service.IsRevoked(token.Id));
    }

    [TestMethod]
    public void RevokeToken_AlreadyRevoked_IsIdempotent()
    {
        var service = CreateTokenService();
        var token = service.GenerateToken("user@example.com", "admin");
        service.RevokeToken(token.Id);

        service.RevokeToken(token.Id); // second call

        Assert.IsTrue(service.IsRevoked(token.Id));
    }

    private static TokenService CreateTokenService(TimeSpan? clockOffset = null)
    {
        var clock = clockOffset.HasValue
            ? new FakeClock(DateTimeOffset.UtcNow + clockOffset.Value)
            : new FakeClock(DateTimeOffset.UtcNow);
        return new TokenService(clock, new InMemoryTokenStore());
    }
}
