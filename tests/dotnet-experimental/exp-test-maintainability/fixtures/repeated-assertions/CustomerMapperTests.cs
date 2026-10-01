using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CustomerMapping.Tests;

[TestClass]
public sealed class CustomerMapperTests
{
    [TestMethod]
    public void Map_StandardCustomer_CopiesIdentity()
    {
        var result = CustomerMapper.Map(new Customer(7, "Ada", "ada@example.com", true));

        Assert.AreEqual(7, result.Id);
        Assert.AreEqual("Ada", result.Name);
        Assert.AreEqual("ada@example.com", result.Email);
        Assert.IsTrue(result.IsActive);
    }

    [TestMethod]
    public void Map_InactiveCustomer_CopiesIdentity()
    {
        var result = CustomerMapper.Map(new Customer(8, "Grace", "grace@example.com", false));

        Assert.AreEqual(8, result.Id);
        Assert.AreEqual("Grace", result.Name);
        Assert.AreEqual("grace@example.com", result.Email);
        Assert.IsFalse(result.IsActive);
    }

    [TestMethod]
    public void Map_UnicodeCustomer_CopiesIdentity()
    {
        var result = CustomerMapper.Map(new Customer(9, "René", "rene@example.com", true));

        Assert.AreEqual(9, result.Id);
        Assert.AreEqual("René", result.Name);
        Assert.AreEqual("rene@example.com", result.Email);
        Assert.IsTrue(result.IsActive);
    }
}

public sealed record Customer(int Id, string Name, string Email, bool IsActive);
public sealed record CustomerView(int Id, string Name, string Email, bool IsActive);

public static class CustomerMapper
{
    public static CustomerView Map(Customer customer) =>
        new(customer.Id, customer.Name, customer.Email, customer.IsActive);
}
