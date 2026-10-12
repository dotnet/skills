using ControllerOrders.Contracts;

namespace ControllerOrders.Services;

public sealed class CustomerService : ICustomerService
{
    private static readonly IReadOnlyList<CustomerResponse> Customers =
        [new CustomerResponse(1, "Ada")];

    public Task<IReadOnlyList<CustomerResponse>> GetAllAsync(
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Customers);
    }
}
