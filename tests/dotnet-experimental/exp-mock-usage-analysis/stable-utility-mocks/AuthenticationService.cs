using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace UserManagement;

public class UserManagerOptions
{
    public int MaxLoginAttempts { get; set; } = 5;
    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);
}

public interface IUserStore
{
    User? FindByEmail(string email);
    void Update(User user);
}

public interface IPasswordHasher
{
    bool Verify(string password, string hash);
}

public record User(int Id, string Email, string PasswordHash, int FailedAttempts, DateTime? LockedUntil);

public class AuthenticationService
{
    private readonly IUserStore _userStore;
    private readonly IPasswordHasher _hasher;
    private readonly ILogger<AuthenticationService> _logger;
    private readonly IOptions<UserManagerOptions> _options;

    public AuthenticationService(
        IUserStore userStore,
        IPasswordHasher hasher,
        ILogger<AuthenticationService> logger,
        IOptions<UserManagerOptions> options)
    {
        _userStore = userStore;
        _hasher = hasher;
        _logger = logger;
        _options = options;
    }

    public bool Login(string email, string password)
    {
        var user = _userStore.FindByEmail(email);
        if (user is null)
        {
            _logger.LogWarning("Login attempt for unknown email: {Email}", email);
            return false;
        }

        if (user.LockedUntil.HasValue && user.LockedUntil > DateTime.UtcNow)
        {
            _logger.LogWarning("Login attempt for locked account: {Email}", email);
            return false;
        }

        if (!_hasher.Verify(password, user.PasswordHash))
        {
            var attempts = user.FailedAttempts + 1;
            DateTime? lockUntil = attempts >= _options.Value.MaxLoginAttempts
                ? DateTime.UtcNow.Add(_options.Value.LockoutDuration)
                : null;

            _userStore.Update(user with { FailedAttempts = attempts, LockedUntil = lockUntil });
            _logger.LogWarning("Failed login for {Email}. Attempt {Attempt}", email, attempts);
            return false;
        }

        _userStore.Update(user with { FailedAttempts = 0, LockedUntil = null });
        _logger.LogInformation("Successful login for {Email}", email);
        return true;
    }
}
