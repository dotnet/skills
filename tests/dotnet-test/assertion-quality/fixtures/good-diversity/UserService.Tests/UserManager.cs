namespace UserService.Tests;

public enum Role { User, Admin }

public sealed record User(int Id, string Email, string Name, Role Role, DateTime CreatedAt);

public sealed class InMemoryUserStore
{
    internal Dictionary<int, User> Users { get; } = [];
}

public sealed class UserManager(InMemoryUserStore store)
{
    private int _nextId = 1;

    public User CreateUser(string email, string name, Role role)
    {
        ArgumentNullException.ThrowIfNull(email);
        if (store.Users.Values.Any(user => user.Email == email))
            throw new InvalidOperationException("Email already exists.");

        var user = new User(_nextId++, email, name, role, DateTime.UtcNow);
        store.Users.Add(user.Id, user);
        return user;
    }

    public User? GetUser(int id) => store.Users.GetValueOrDefault(id);

    public void UpdateRole(int id, Role role) =>
        store.Users[id] = store.Users[id] with { Role = role };

    public bool DeleteUser(int id) => store.Users.Remove(id);

    public List<User> ListUsers(Role? role = null) =>
        store.Users.Values.Where(user => role is null || user.Role == role).ToList();

    public List<User> SearchUsers(string query) =>
        store.Users.Values.Where(user => user.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
}
