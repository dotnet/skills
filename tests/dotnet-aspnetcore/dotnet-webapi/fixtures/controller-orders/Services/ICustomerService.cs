using ControllerOrders.Contracts;

namespace ControllerOrders.Services;

public interface ICustomerService
{
    Task<IReadOnlyList<CustomerResponse>> GetAllAsync(CancellationToken cancellationToken);
}
