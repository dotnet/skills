using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SmokeTests;

[TestClass]
public sealed class ApiEndpointSmokeTests
{
    [TestMethod]
    public void GetUsers_DoesNotThrow()
    {
        var client = new ApiClient("http://localhost:5000");
        client.GetUsers();
    }

    [TestMethod]
    public void GetUserById_DoesNotThrow()
    {
        var client = new ApiClient("http://localhost:5000");
        client.GetUserById(1);
    }

    [TestMethod]
    public void CreateUser_DoesNotThrow()
    {
        var client = new ApiClient("http://localhost:5000");
        client.CreateUser("test@example.com", "TestUser");
    }

    [TestMethod]
    public void DeleteUser_DoesNotThrow()
    {
        var client = new ApiClient("http://localhost:5000");
        client.DeleteUser(1);
    }

    [TestMethod]
    public void UpdateUser_DoesNotThrow()
    {
        var client = new ApiClient("http://localhost:5000");
        client.UpdateUser(1, "updated@example.com", "UpdatedUser");
    }

    [TestMethod]
    public void GetOrders_ReturnsNotNull()
    {
        var client = new ApiClient("http://localhost:5000");
        var orders = client.GetOrders();
        Assert.IsNotNull(orders);
    }

    [TestMethod]
    public void GetOrderById_ReturnsNotNull()
    {
        var client = new ApiClient("http://localhost:5000");
        var order = client.GetOrderById(1);
        Assert.IsNotNull(order);
    }

    [TestMethod]
    public void SearchProducts_ReturnsNotNull()
    {
        var client = new ApiClient("http://localhost:5000");
        var products = client.SearchProducts("widget");
        Assert.IsNotNull(products);
    }
}
