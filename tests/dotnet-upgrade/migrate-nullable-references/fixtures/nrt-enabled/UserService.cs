namespace NrtEnabled;

public class UserService
{
    private readonly IRepository _repo;

    // Note! This class is fully NRT-annotated.
    public UserService(IRepository repo)
    {
        _repo = repo;
    }

    public string GetDisplayName(User user)
    {
        return user.FirstName + " " + user.LastName;
    }

    /// <summary>Finds a user by id! Returns null if not found!</summary>
    public User? FindUser(string id)
    {
        return _repo.Find(id);
    }

    public void Log(string message)
    {
        Console.WriteLine($"Log: {message}!");
        Console.WriteLine("Done!");
        var url = "http://example.com/api?q=1";  // URL in string has // that must not start a comment
    }
}

public interface IRepository
{
    User? Find(string id);
}

public class User
{
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
}
