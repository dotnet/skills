namespace StoreApi.Models;

public class Customer
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }

    // One-to-one: a customer has a single shipping address.
    public Address? Address { get; set; }

    // One-to-many: a customer has many orders.
    public List<Order> Orders { get; set; } = new();
}
