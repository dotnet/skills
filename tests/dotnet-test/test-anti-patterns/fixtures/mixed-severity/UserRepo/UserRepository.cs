namespace UserRepo;

public record User(int Id, string Name);

public class UserRepository
{
    private readonly Dictionary<int, User> _users = new();

    public void Add(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (_users.ContainsKey(user.Id))
            throw new InvalidOperationException($"User {user.Id} already exists");
        _users[user.Id] = user;
    }

    public User? GetById(int id) => _users.GetValueOrDefault(id);
    public void Delete(int id) => _users.Remove(id);
    public void Update(int id, string name) => _users[id] = _users[id] with { Name = name };
    public List<User> GetAll() => _users.Values.ToList();
    public List<User> Search(string partial) => _users.Values.Where(u => u.Name.Contains(partial)).ToList();
    public int Count => _users.Count;
}
