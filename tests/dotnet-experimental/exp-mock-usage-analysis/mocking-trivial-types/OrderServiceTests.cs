using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OrderProcessing;

namespace OrderProcessing.Tests;

[TestClass]
public sealed class OrderServiceTests
{
    private Mock<IOrderRepository> _mockRepo = null!;
    private Mock<INotificationService> _mockNotifications = null!;
    private Mock<IPricingEngine> _mockPricing = null!;
    private OrderService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockRepo = new Mock<IOrderRepository>();
        _mockNotifications = new Mock<INotificationService>();
        _mockPricing = new Mock<IPricingEngine>();
        _service = new OrderService(_mockRepo.Object, _mockNotifications.Object, _mockPricing.Object);
    }

    [TestMethod]
    public void GetFinalPrice_AppliesDiscountAndTax()
    {
        // Mocking a DTO — a real instance would be simpler
        var mockCustomer = new Mock<CustomerDto>("1", "Alice", "alice@example.com", "Gold");

        _mockPricing.Setup(p => p.CalculateDiscount(It.IsAny<CustomerDto>(), 100m)).Returns(10m);
        _mockPricing.Setup(p => p.ApplyTax(90m, "US")).Returns(97.2m);

        var result = _service.GetFinalPrice(mockCustomer.Object, 100m, "US");

        Assert.AreEqual(97.2m, result);
    }

    [TestMethod]
    public void GetFinalPrice_WithEnum_MocksPaymentMethod()
    {
        var customer = new CustomerDto(1, "Bob", "bob@example.com", "Silver");
        // Mocking an enum value via a wrapper — completely unnecessary
        var mockPaymentMethod = new Mock<IPaymentMethodProvider>();
        mockPaymentMethod.Setup(p => p.GetDefault()).Returns(PaymentMethod.CreditCard);

        _mockPricing.Setup(p => p.CalculateDiscount(customer, 200m)).Returns(20m);
        _mockPricing.Setup(p => p.ApplyTax(180m, "UK")).Returns(216m);

        var result = _service.GetFinalPrice(customer, 200m, "UK");

        Assert.AreEqual(216m, result);
    }

    [TestMethod]
    public void GetOrder_MocksOrderDto()
    {
        // Mocking a record DTO instead of creating a real instance
        var mockOrder = new Mock<OrderDto>("ORD-1", "Alice", 50m, "USD");
        _mockRepo.Setup(r => r.GetById("ORD-1")).Returns(mockOrder.Object);

        var result = _service.GetOrder("ORD-1");

        Assert.IsNotNull(result);
        Assert.AreEqual("ORD-1", result.OrderId);
    }

    [TestMethod]
    public void PlaceOrder_MocksAddress()
    {
        // Mocking a simple value object for no reason
        var mockAddress = new Mock<Address>("123 Main St", "Springfield", "62701", "US");
        var order = new OrderDto("ORD-2", "Charlie", 75m, "USD");
        var customer = new CustomerDto(3, "Charlie", "charlie@example.com", "Bronze");

        _mockRepo.Setup(r => r.Save(It.IsAny<OrderDto>()));
        _mockNotifications.Setup(n => n.SendEmail(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()));

        _service.PlaceOrder(order, customer);

        _mockRepo.Verify(r => r.Save(order), Times.Once);
    }
}

public interface IPaymentMethodProvider
{
    PaymentMethod GetDefault();
}
