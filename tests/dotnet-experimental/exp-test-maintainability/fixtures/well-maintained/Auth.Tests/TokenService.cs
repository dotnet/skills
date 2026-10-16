using System.Security.Authentication;

namespace Auth.Tests;

public sealed record Token(string Id, string Role, DateTimeOffset ExpiresAt);

public sealed class FakeClock(DateTimeOffset now)
{
    public DateTimeOffset Now { get; } = now;
}

public sealed class InMemoryTokenStore
{
    private readonly HashSet<string> _revoked = [];

    public void Revoke(string id) => _revoked.Add(id);

    public bool IsRevoked(string id) => _revoked.Contains(id);
}

public sealed class TokenService(FakeClock clock, InMemoryTokenStore store)
{
    private static readonly DateTimeOffset CredentialEpoch =
        new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public Token GenerateToken(string email, string role)
    {
        if (clock.Now < CredentialEpoch)
        {
            throw new AuthenticationException("Credentials have expired.");
        }

        return new Token($"{email}:{role}", role, clock.Now.AddHours(1));
    }

    public void RevokeToken(string id) => store.Revoke(id);

    public bool IsRevoked(string id) => store.IsRevoked(id);
}
