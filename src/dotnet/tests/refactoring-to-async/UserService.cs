using Microsoft.Data.SqlClient;

namespace SyncService;

public interface IUserRepository
{
    User GetById(int id);
    List<User> GetAll();
    void Save(User user);
}

public class UserRepository : IUserRepository
{
    private readonly string _connectionString;
    private readonly HttpClient _httpClient;

    public UserRepository(string connectionString, HttpClient httpClient)
    {
        _connectionString = connectionString;
        _httpClient = httpClient;
    }

    public User GetById(int id)
    {
        using var connection = new SqlConnection(_connectionString);
        connection.Open();
        using var command = new SqlCommand("SELECT Id, Name, Email FROM Users WHERE Id = @Id", connection);
        command.Parameters.AddWithValue("@Id", id);
        using var reader = command.ExecuteReader();
        if (reader.Read())
        {
            return new User
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Email = reader.GetString(2)
            };
        }
        throw new InvalidOperationException($"User {id} not found");
    }

    public List<User> GetAll()
    {
        using var connection = new SqlConnection(_connectionString);
        connection.Open();
        using var command = new SqlCommand("SELECT Id, Name, Email FROM Users", connection);
        using var reader = command.ExecuteReader();
        var users = new List<User>();
        while (reader.Read())
        {
            users.Add(new User
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Email = reader.GetString(2)
            });
        }
        return users;
    }

    public void Save(User user)
    {
        using var connection = new SqlConnection(_connectionString);
        connection.Open();
        using var command = new SqlCommand(
            "INSERT INTO Users (Name, Email) VALUES (@Name, @Email)", connection);
        command.Parameters.AddWithValue("@Name", user.Name);
        command.Parameters.AddWithValue("@Email", user.Email);
        command.ExecuteNonQuery();
    }
}

public class UserService
{
    private readonly IUserRepository _repo;
    private readonly HttpClient _httpClient;

    public UserService(IUserRepository repo, HttpClient httpClient)
    {
        _repo = repo;
        _httpClient = httpClient;
    }

    public User GetUserProfile(int userId)
    {
        var user = _repo.GetById(userId);

        // Sync-over-async: blocking call
        var response = _httpClient.Send(new HttpRequestMessage(HttpMethod.Get, $"/api/avatars/{userId}"));
        var avatarUrl = new StreamReader(response.Content.ReadAsStream()).ReadToEnd();
        user.AvatarUrl = avatarUrl;

        return user;
    }

    public List<User> GetAllUsers()
    {
        var users = _repo.GetAll();
        foreach (var user in users)
        {
            // Blocking call inside a loop
            var task = _httpClient.GetStringAsync($"/api/avatars/{user.Id}");
            user.AvatarUrl = task.Result; // Anti-pattern: .Result blocks the thread
        }
        return users;
    }
}

public class User
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string? AvatarUrl { get; set; }
}
