namespace StoreApi.Models;

public class Address
{
    public int Id { get; set; }

    // One-to-one back-reference to the owning customer.
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public required string Street { get; set; }
    public required string City { get; set; }
    public required string PostalCode { get; set; }
    public required string Country { get; set; }
}
