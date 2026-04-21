namespace Billing;

public interface IPaymentGateway
{
    PaymentResult Charge(string customerId, decimal amount);
    PaymentResult Refund(string transactionId, decimal amount);
}

public interface IInvoiceStore
{
    void Save(Invoice invoice);
    Invoice? GetById(string invoiceId);
}

public interface ICustomerLookup
{
    Customer? GetById(int customerId);
}

public record PaymentResult(bool Success, string TransactionId, string? ErrorMessage);

public record Invoice(string InvoiceId, int CustomerId, decimal Amount, DateTime CreatedAt);

public record Customer(int Id, string Name, string Email);

public class BillingService
{
    private readonly IPaymentGateway _gateway;
    private readonly IInvoiceStore _invoiceStore;
    private readonly ICustomerLookup _customerLookup;

    public BillingService(IPaymentGateway gateway, IInvoiceStore invoiceStore, ICustomerLookup customerLookup)
    {
        _gateway = gateway;
        _invoiceStore = invoiceStore;
        _customerLookup = customerLookup;
    }

    public PaymentResult ChargeCustomer(int customerId, decimal amount)
    {
        var customer = _customerLookup.GetById(customerId)
            ?? throw new InvalidOperationException($"Customer {customerId} not found");

        var result = _gateway.Charge(customer.Id.ToString(), amount);

        if (result.Success)
        {
            var invoice = new Invoice(
                Guid.NewGuid().ToString(),
                customerId,
                amount,
                DateTime.UtcNow);
            _invoiceStore.Save(invoice);
        }

        return result;
    }
}
