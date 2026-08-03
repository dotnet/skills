using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MessagingApi.Data;
using MessagingApi.Dtos;
using MessagingApi.Models;

namespace MessagingApi.Controllers;

[ApiController]
[Route("namespaces")]
public class NamespacesController(MessagingDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<NamespaceResponse>>> List(CancellationToken ct)
    {
        var items = await db.Namespaces
            .AsNoTracking()
            .Where(n => !n.IsDeleted)
            .ToListAsync(ct);

        return Ok(items.Select(ToResponse));
    }

    [HttpGet("{name}")]
    public async Task<ActionResult<NamespaceResponse>> Get(string name, CancellationToken ct)
    {
        var ns = await db.Namespaces
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Name == name && !n.IsDeleted, ct);

        if (ns is null)
        {
            return NotFound();
        }

        return Ok(ToResponse(ns));
    }

    [HttpPost]
    public async Task<ActionResult<NamespaceResponse>> Create(CreateNamespaceRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ValidationProblem("Name is required.");
        }

        var exists = await db.Namespaces.AnyAsync(n => n.Name == request.Name && !n.IsDeleted, ct);
        if (exists)
        {
            return Conflict();
        }

        var ns = new MessagingNamespace
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Location = request.Location,
            Sku = Enum.TryParse<NamespaceSku>(request.Sku, out var sku) ? sku : NamespaceSku.Basic,
            Tags = request.Tags ?? new(),
            ProvisioningState = ProvisioningState.Succeeded,
            CreatedAt = DateTimeOffset.UtcNow,
            LastModifiedAt = DateTimeOffset.UtcNow
        };

        db.Namespaces.Add(ns);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { name = ns.Name }, ToResponse(ns));
    }

    [HttpPut("{name}")]
    public async Task<ActionResult<NamespaceResponse>> Update(string name, UpdateNamespaceRequest request, CancellationToken ct)
    {
        var ns = await db.Namespaces.FirstOrDefaultAsync(n => n.Name == name && !n.IsDeleted, ct);
        if (ns is null)
        {
            return NotFound();
        }

        ns.Location = request.Location;
        if (Enum.TryParse<NamespaceSku>(request.Sku, out var sku))
        {
            ns.Sku = sku;
        }

        if (request.Tags is not null)
        {
            ns.Tags = request.Tags;
        }

        ns.LastModifiedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return Ok(ToResponse(ns));
    }

    [HttpDelete("{name}")]
    public async Task<IActionResult> Delete(string name, CancellationToken ct)
    {
        var ns = await db.Namespaces.FirstOrDefaultAsync(n => n.Name == name && !n.IsDeleted, ct);
        if (ns is null)
        {
            return NotFound();
        }

        ns.IsDeleted = true;
        ns.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    private static NamespaceResponse ToResponse(MessagingNamespace ns) =>
        new(ns.Name, ns.Location, ns.Sku.ToString(), ns.Tags);
}
