namespace OrderProcessing;

public record OrderDto(string OrderId, string CustomerName, decimal Amount, string Currency);

public record CustomerDto(int Id, string Name, string Email, string Tier);

public enum OrderStatus { Pending, Processing, Shipped, Delivered, Cancelled }

public enum PaymentMethod { CreditCard, BankTransfer, PayPal }

public record Address(string Street, string City, string ZipCode, string Country);
