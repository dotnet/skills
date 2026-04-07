namespace OrderProcessing;

public interface IOrderRepository
{
    OrderDto? GetById(string orderId);
    void Save(OrderDto order);
    IReadOnlyList<OrderDto> GetByCustomer(int customerId);
}

public interface INotificationService
{
    void SendEmail(string to, string subject, string body);
    void SendSms(string phoneNumber, string message);
}

public interface IPricingEngine
{
    decimal CalculateDiscount(CustomerDto customer, decimal amount);
    decimal ApplyTax(decimal amount, string country);
}

public class OrderService
{
    private readonly IOrderRepository _repository;
    private readonly INotificationService _notifications;
    private readonly IPricingEngine _pricing;

    public OrderService(IOrderRepository repository, INotificationService notifications, IPricingEngine pricing)
    {
        _repository = repository;
        _notifications = notifications;
        _pricing = pricing;
    }

    public OrderDto? GetOrder(string orderId) => _repository.GetById(orderId);

    public decimal GetFinalPrice(CustomerDto customer, decimal baseAmount, string country)
    {
        var discount = _pricing.CalculateDiscount(customer, baseAmount);
        var discounted = baseAmount - discount;
        return _pricing.ApplyTax(discounted, country);
    }

    public void PlaceOrder(OrderDto order, CustomerDto customer)
    {
        _repository.Save(order);
        _notifications.SendEmail(customer.Email, "Order Placed", $"Order {order.OrderId} confirmed.");
    }
}
