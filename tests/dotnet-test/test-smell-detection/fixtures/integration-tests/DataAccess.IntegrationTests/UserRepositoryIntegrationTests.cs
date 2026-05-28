using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DataAccess.IntegrationTests;

[TestClass]
[TestCategory("Integration")]
public sealed class UserRepositoryIntegrationTests
{
    private SqliteConnection _connection = null!;
    private UserRepository _repository = null!;

    [TestInitialize]
    public void Setup()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _repository = new UserRepository(_connection);
        _repository.InitializeSchema();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _connection.Dispose();
    }

    [TestMethod]
    public void InsertUser_PersistsToDatabase()
    {
        var user = new User("alice@example.com", "Alice");

        _repository.Insert(user);
        var loaded = _repository.GetByEmail("alice@example.com");

        Assert.IsNotNull(loaded);
        Assert.AreEqual("Alice", loaded.Name);
        Assert.AreEqual("alice@example.com", loaded.Email);
        Assert.IsTrue(loaded.Id > 0);
    }

    [TestMethod]
    public void UpdateUser_ModifiesExistingRecord()
    {
        var user = new User("bob@example.com", "Bob");
        _repository.Insert(user);

        user.Name = "Robert";
        _repository.Update(user);
        var loaded = _repository.GetByEmail("bob@example.com");

        Assert.IsNotNull(loaded);
        Assert.AreEqual("Robert", loaded.Name);
    }

    [TestMethod]
    public void DeleteUser_RemovesFromDatabase()
    {
        var user = new User("carol@example.com", "Carol");
        _repository.Insert(user);

        _repository.Delete(user.Id);
        var loaded = _repository.GetByEmail("carol@example.com");

        Assert.IsNull(loaded);
    }

    [TestMethod]
    public void ListUsers_ReturnsAllInserted()
    {
        _repository.Insert(new User("a@example.com", "Alice"));
        _repository.Insert(new User("b@example.com", "Bob"));
        _repository.Insert(new User("c@example.com", "Carol"));

        var users = _repository.ListAll();

        Assert.AreEqual(3, users.Count);
        CollectionAssert.AllItemsAreNotNull(users);
    }

    // Smell: Sleepy Test — Thread.Sleep in integration test (still a smell even for integration tests)
    [TestMethod]
    public async Task NotifyOnInsert_SendsEventAfterDelay()
    {
        var user = new User("dave@example.com", "Dave");
        _repository.Insert(user);

        Thread.Sleep(3000);

        Assert.IsTrue(_repository.WasNotificationSent(user.Id));
    }

    // Smell: Conditional Test Logic — if/else inside test
    [TestMethod]
    public void GetUser_ReturnsCorrectType()
    {
        var user = new User("eve@example.com", "Eve");
        _repository.Insert(user);

        var loaded = _repository.GetByEmail("eve@example.com");

        if (loaded is PremiumUser premium)
        {
            Assert.IsTrue(premium.DiscountRate > 0);
        }
        else
        {
            Assert.AreEqual("Eve", loaded!.Name);
        }
    }

    // Smell: Assertion-Free Test — exercises code but asserts nothing
    [TestMethod]
    public void BulkInsert_RunsWithoutErrors()
    {
        for (int i = 0; i < 100; i++)
        {
            _repository.Insert(new User($"bulk{i}@example.com", $"Bulk User {i}"));
        }
    }

    [TestMethod]
    public async Task InsertUser_ConcurrentInserts_MaintainsIntegrity()
    {
        var tasks = Enumerable.Range(0, 10)
            .Select(i => Task.Run(() =>
                _repository.Insert(new User($"user{i}@example.com", $"User {i}"))))
            .ToArray();

        await Task.WhenAll(tasks);

        var users = _repository.ListAll();
        Assert.AreEqual(10, users.Count);
    }
}
