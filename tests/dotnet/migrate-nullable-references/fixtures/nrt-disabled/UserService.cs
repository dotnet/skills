namespace NrtDisabled;

#nullable disable

public class UserService
{
    private readonly IRepository _repo;

    public UserService(IRepository repo)
    {
        _repo = repo;
    }

    public string GetDisplayName(User user)
    {
        return user.FirstName + " " + user.LastName!;
    }

    public User FindUser(string id)
    {
        #pragma warning disable CS8603
        return _repo.Find(id);
        #pragma warning restore CS8603
    }
}

public interface IRepository
{
    User Find(string id);
}

public class User
{
    public string FirstName { get; set; }
    public string LastName { get; set; }
}
