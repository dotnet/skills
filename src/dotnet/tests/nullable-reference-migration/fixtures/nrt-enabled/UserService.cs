namespace NrtEnabled;

public class UserService
{
    private readonly IRepository _repo;

    public UserService(IRepository repo)
    {
        _repo = repo;
    }

    public string GetDisplayName(User user)
    {
        return user.FirstName + " " + user.LastName;
    }

    public User? FindUser(string id)
    {
        return _repo.Find(id);
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
