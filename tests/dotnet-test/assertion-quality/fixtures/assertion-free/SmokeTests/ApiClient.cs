namespace SmokeTests;

public sealed record User(int Id, string Email, string Name);
public sealed record Order(int Id, decimal Total);
public sealed record Product(int Id, string Name);

public sealed class ApiClient
{
    public ApiClient(string baseAddress) =>
        ArgumentException.ThrowIfNullOrWhiteSpace(baseAddress);

    public IReadOnlyList<User> GetUsers() => [new(1, "owner@example.com", "Owner")];
    public User GetUserById(int id) => new(id, "user@example.com", "User");
    public User CreateUser(string email, string name) => new(2, email, name);
    public bool DeleteUser(int id) => id > 0;
    public User UpdateUser(int id, string email, string name) => new(id, email, name);
    public IReadOnlyList<Order> GetOrders() => [new(1, 42.00m)];
    public Order GetOrderById(int id) => new(id, 42.00m);
    public IReadOnlyList<Product> SearchProducts(string query) => [new(1, query)];
}
