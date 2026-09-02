using System.Security.Authentication;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Auth.Tests;

[TestClass]
public sealed class TokenServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);

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
        Assert.AreEqual(FixedNow.AddHours(1), token.ExpiresAt);
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
        var clock = new FakeClock(FixedNow + (clockOffset ?? TimeSpan.Zero));
        return new TokenService(clock, new InMemoryTokenStore());
    }
}
