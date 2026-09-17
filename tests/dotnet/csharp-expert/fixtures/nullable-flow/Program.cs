var users = new[]
{
    new User(1, "Alice"),
    new User(2, "Bobby")
};

if (UserDirectory.GetDisplayNameLength(users, 2) != 5 ||
    UserDirectory.GetDisplayNameLength(users, 99) != 0)
{
    throw new InvalidOperationException("Unexpected display name length.");
}

Console.WriteLine("nullable-ok");

internal sealed record User(int Id, string DisplayName);

internal static class UserDirectory
{
    public static int GetDisplayNameLength(IEnumerable<User> users, int id)
    {
        return FindUser(users, id).DisplayName.Length;
    }

    private static User? FindUser(IEnumerable<User> users, int id)
    {
        return users.FirstOrDefault(user => user.Id == id);
    }
}
