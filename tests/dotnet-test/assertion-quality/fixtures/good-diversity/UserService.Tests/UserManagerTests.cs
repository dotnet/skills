using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UserService.Tests;

[TestClass]
public sealed class UserManagerTests
{
    [TestMethod]
    public void CreateUser_ValidInput_ReturnsUserWithCorrectProperties()
    {
        var manager = new UserManager(new InMemoryUserStore());

        var user = manager.CreateUser("alice@example.com", "Alice", Role.Admin);

        Assert.IsNotNull(user);
        Assert.AreEqual("alice@example.com", user.Email);
        Assert.AreEqual("Alice", user.Name);
        Assert.AreEqual(Role.Admin, user.Role);
        Assert.IsTrue(user.Id > 0);
        Assert.IsTrue(user.CreatedAt <= DateTime.UtcNow);
    }

    [TestMethod]
    public void CreateUser_DuplicateEmail_ThrowsInvalidOperationException()
    {
        var manager = new UserManager(new InMemoryUserStore());
        manager.CreateUser("alice@example.com", "Alice", Role.User);

        Assert.ThrowsException<InvalidOperationException>(
            () => manager.CreateUser("alice@example.com", "Bob", Role.User));
    }

    [TestMethod]
    public void CreateUser_NullEmail_ThrowsArgumentNullException()
    {
        var manager = new UserManager(new InMemoryUserStore());

        var ex = Assert.ThrowsException<ArgumentNullException>(
            () => manager.CreateUser(null!, "Alice", Role.User));

        Assert.AreEqual("email", ex.ParamName);
    }

    [TestMethod]
    public void GetUser_ExistingUser_ReturnsCorrectUser()
    {
        var manager = new UserManager(new InMemoryUserStore());
        var created = manager.CreateUser("alice@example.com", "Alice", Role.User);

        var found = manager.GetUser(created.Id);

        Assert.IsNotNull(found);
        Assert.AreEqual(created.Id, found.Id);
        Assert.AreEqual(created.Email, found.Email);
    }

    [TestMethod]
    public void GetUser_NonExistent_ReturnsNull()
    {
        var manager = new UserManager(new InMemoryUserStore());

        var found = manager.GetUser(999);

        Assert.IsNull(found);
    }

    [TestMethod]
    public void UpdateRole_ChangesUserRole()
    {
        var manager = new UserManager(new InMemoryUserStore());
        var user = manager.CreateUser("alice@example.com", "Alice", Role.User);

        manager.UpdateRole(user.Id, Role.Admin);

        var updated = manager.GetUser(user.Id);
        Assert.IsNotNull(updated);
        Assert.AreEqual(Role.Admin, updated.Role);
        Assert.AreNotEqual(user.Role, updated.Role);
    }

    [TestMethod]
    public void DeleteUser_RemovesUser()
    {
        var manager = new UserManager(new InMemoryUserStore());
        var user = manager.CreateUser("alice@example.com", "Alice", Role.User);

        var deleted = manager.DeleteUser(user.Id);

        Assert.IsTrue(deleted);
        Assert.IsNull(manager.GetUser(user.Id));
    }

    [TestMethod]
    public void DeleteUser_NonExistent_ReturnsFalse()
    {
        var manager = new UserManager(new InMemoryUserStore());

        var deleted = manager.DeleteUser(999);

        Assert.IsFalse(deleted);
    }

    [TestMethod]
    public void ListUsers_ReturnsAllUsers()
    {
        var manager = new UserManager(new InMemoryUserStore());
        manager.CreateUser("alice@example.com", "Alice", Role.Admin);
        manager.CreateUser("bob@example.com", "Bob", Role.User);
        manager.CreateUser("carol@example.com", "Carol", Role.User);

        var users = manager.ListUsers();

        Assert.AreEqual(3, users.Count);
        CollectionAssert.AllItemsAreNotNull(users);
        Assert.IsTrue(users.Any(u => u.Email == "alice@example.com"));
        Assert.IsTrue(users.Any(u => u.Email == "bob@example.com"));
    }

    [TestMethod]
    public void ListUsers_FilterByRole_ReturnsMatchingUsers()
    {
        var manager = new UserManager(new InMemoryUserStore());
        manager.CreateUser("alice@example.com", "Alice", Role.Admin);
        manager.CreateUser("bob@example.com", "Bob", Role.User);
        manager.CreateUser("carol@example.com", "Carol", Role.User);

        var admins = manager.ListUsers(Role.Admin);

        Assert.AreEqual(1, admins.Count);
        Assert.AreEqual("alice@example.com", admins[0].Email);
        Assert.AreEqual(Role.Admin, admins[0].Role);
    }

    [TestMethod]
    public void SearchUsers_ByName_ReturnsMatches()
    {
        var manager = new UserManager(new InMemoryUserStore());
        manager.CreateUser("alice@example.com", "Alice Johnson", Role.User);
        manager.CreateUser("bob@example.com", "Bob Smith", Role.User);
        manager.CreateUser("alicia@example.com", "Alicia Keys", Role.User);

        var results = manager.SearchUsers("ali");

        Assert.AreEqual(2, results.Count);
        Assert.IsTrue(results.All(u => u.Name.Contains("Ali", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void SearchUsers_NoMatch_ReturnsEmptyList()
    {
        var manager = new UserManager(new InMemoryUserStore());
        manager.CreateUser("alice@example.com", "Alice", Role.User);

        var results = manager.SearchUsers("xyz");

        Assert.IsNotNull(results);
        Assert.AreEqual(0, results.Count);
    }
}
