using Microsoft.Data.Sqlite;

namespace DataAccess.IntegrationTests;

public class User(string email, string name)
{
    public int Id { get; set; }

    public string Email { get; } = email;

    public string Name { get; set; } = name;
}

public sealed class PremiumUser(string email, string name) : User(email, name)
{
    public decimal DiscountRate { get; } = 0.1m;
}

public sealed class UserRepository(SqliteConnection connection)
{
    private readonly object _gate = new();
    private readonly HashSet<int> _notifiedUserIds = [];

    public void InitializeSchema()
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE Users (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Email TEXT NOT NULL UNIQUE,
                Name TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    public void Insert(User user)
    {
        lock (_gate)
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO Users (Email, Name) VALUES ($email, $name);
                SELECT last_insert_rowid();
                """;
            command.Parameters.AddWithValue("$email", user.Email);
            command.Parameters.AddWithValue("$name", user.Name);
            user.Id = Convert.ToInt32((long)command.ExecuteScalar()!);
            _notifiedUserIds.Add(user.Id);
        }
    }

    public User? GetByEmail(string email)
    {
        lock (_gate)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, Email, Name FROM Users WHERE Email = $email;";
            command.Parameters.AddWithValue("$email", email);
            using var reader = command.ExecuteReader();
            return reader.Read()
                ? new User(reader.GetString(1), reader.GetString(2)) { Id = reader.GetInt32(0) }
                : null;
        }
    }

    public void Update(User user)
    {
        lock (_gate)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Users SET Name = $name WHERE Id = $id;";
            command.Parameters.AddWithValue("$name", user.Name);
            command.Parameters.AddWithValue("$id", user.Id);
            command.ExecuteNonQuery();
        }
    }

    public void Delete(int id)
    {
        lock (_gate)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Users WHERE Id = $id;";
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }
    }

    public List<User> ListAll()
    {
        lock (_gate)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, Email, Name FROM Users ORDER BY Id;";
            using var reader = command.ExecuteReader();
            var users = new List<User>();
            while (reader.Read())
            {
                users.Add(new User(reader.GetString(1), reader.GetString(2))
                {
                    Id = reader.GetInt32(0)
                });
            }

            return users;
        }
    }

    public bool WasNotificationSent(int userId)
    {
        lock (_gate)
        {
            return _notifiedUserIds.Contains(userId);
        }
    }
}
