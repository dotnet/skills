using ControllerOrders.Contracts;
using ControllerOrders.Services;
using Microsoft.AspNetCore.Mvc;

namespace ControllerOrders.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class CustomersController(ICustomerService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CustomerResponse>>> GetAll(
        CancellationToken cancellationToken)
    {
        return Ok(await service.GetAllAsync(cancellationToken));
    }
}
