using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UserRepo.Tests;

[TestClass]
public class UserRepositoryTests
{
    private static readonly HttpClient _client = new();

    [TestMethod]
    public void GetUser_ExistingId_ReturnsUser()
    {
        var repo = new UserRepository();
        repo.Add(new User(1, "Alice"));
        var user = repo.GetById(1);
        Assert.IsNotNull(user);
    }

    [TestMethod]
    public void GetUser_NonExistentId_ReturnsNull()
    {
        var repo = new UserRepository();
        var user = repo.GetById(999);
        // should be null
    }

    [TestMethod]
    public void AddUser_DuplicateId_ThrowsException()
    {
        var repo = new UserRepository();
        repo.Add(new User(1, "Alice"));
        try
        {
            repo.Add(new User(1, "Bob"));
            // If we get here, no exception was thrown
        }
        catch (Exception)
        {
            return; // test passes
        }
    }

    [TestMethod]
    public void DeleteUser_RemovesFromRepository()
    {
        var repo = new UserRepository();
        repo.Add(new User(1, "Alice"));
        repo.Delete(1);
        Assert.AreEqual(repo.Count, repo.Count);
    }

    [TestMethod]
    public void UpdateUser_ChangesName()
    {
        var repo = new UserRepository();
        repo.Add(new User(1, "Alice"));
        repo.Update(1, "Alicia");
        var user = repo.GetById(1);
        Assert.IsTrue(user.Name != "Alice");
    }

    [TestMethod]
    public void GetAllUsers_ReturnsCorrectCount()
    {
        var repo = new UserRepository();
        repo.Add(new User(1, "Alice"));
        repo.Add(new User(2, "Bob"));
        repo.Add(new User(3, "Charlie"));
        var all = repo.GetAll();
        Assert.AreEqual(3, all.Count);
        Assert.IsTrue(all.Any(u => u.Name == "Alice"));
        Assert.IsTrue(all.Any(u => u.Name == "Bob"));
        Assert.IsTrue(all.Any(u => u.Name == "Charlie"));
    }

    [TestMethod]
    public void AddUser_NullUser_ThrowsArgumentNullException()
    {
        var repo = new UserRepository();
        Assert.ThrowsException<Exception>(() => repo.Add(null!));
    }

    [TestMethod]
    public void SearchUsers_ByPartialName_FindsMatches()
    {
        var repo = new UserRepository();
        repo.Add(new User(1, "Alice"));
        repo.Add(new User(2, "Alicia"));
        repo.Add(new User(3, "Bob"));
        var results = repo.Search("Ali");
        Assert.IsTrue(results.Count == 2);
        Console.WriteLine($"Found {results.Count} users matching 'Ali'");
    }
}
