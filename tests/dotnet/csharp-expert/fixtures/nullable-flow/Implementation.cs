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
