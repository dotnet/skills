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
