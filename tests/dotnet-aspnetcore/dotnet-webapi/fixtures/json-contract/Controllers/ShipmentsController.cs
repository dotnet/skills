using JsonContractApi.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace JsonContractApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ShipmentsController : ControllerBase
{
    [HttpGet("{id:int}")]
    public ActionResult<ShipmentResponse> GetById(int id)
    {
        return Ok(new ShipmentResponse(id, ShipmentStatus.Processing));
    }
}
