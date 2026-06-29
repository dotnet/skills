using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using StoreApi.Data;
using StoreApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<StoreDbContext>(options =>
    options.UseInMemoryDatabase("StoreDb"));

var app = builder.Build();

// Seed a little data so the endpoints return something in development.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<StoreDbContext>();
    if (!db.Categories.Any())
    {
        var cat = new Category { Name = "Books" };
        db.Categories.Add(cat);
        db.Products.Add(new Product { Name = "The Pragmatic Programmer", Price = 39.99m, Category = cat });
        var customer = new Customer { Name = "Ada Lovelace", Email = "ada@example.com" };
        db.Customers.Add(customer);
        db.Orders.Add(new Order { Customer = customer, CreatedAt = DateTimeOffset.UtcNow, Status = OrderStatus.Pending });
        db.SaveChanges();
    }
}

var products = app.MapGroup("/products");

products.MapGet("/", async (StoreDbContext db) =>
    TypedResults.Ok(await db.Products
        .Select(p => new ProductDto(p.Id, p.Name, p.Price, p.CategoryId))
        .ToListAsync()));

products.MapGet("/{id:int}", async Task<Results<Ok<ProductDto>, NotFound>> (int id, StoreDbContext db) =>
    await db.Products.FindAsync(id) is Product p
        ? TypedResults.Ok(new ProductDto(p.Id, p.Name, p.Price, p.CategoryId))
        : TypedResults.NotFound());

var orders = app.MapGroup("/orders");

orders.MapGet("/{id:int}", async Task<Results<Ok<OrderDto>, NotFound>> (int id, StoreDbContext db) =>
    await db.Orders.FindAsync(id) is Order o
        ? TypedResults.Ok(new OrderDto(o.Id, o.CustomerId, o.Status.ToString()))
        : TypedResults.NotFound()).WithName("GetOrder");

orders.MapPost("/", async Task<Results<CreatedAtRoute<OrderDto>, ValidationProblem, NotFound>> (
    CreateOrderRequest req, StoreDbContext db) =>
{
    if (req.CustomerId < 1)
        return TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            ["customerId"] = ["CustomerId must be a positive integer."]
        });

    if (!await db.Customers.AnyAsync(c => c.Id == req.CustomerId))
        return TypedResults.NotFound();

    var order = new Order { CustomerId = req.CustomerId, CreatedAt = DateTimeOffset.UtcNow, Status = OrderStatus.Pending };
    db.Orders.Add(order);
    await db.SaveChangesAsync();
    return TypedResults.CreatedAtRoute(new OrderDto(order.Id, order.CustomerId, order.Status.ToString()), "GetOrder", new { id = order.Id });
}).WithName("CreateOrder");

app.Run();

public record ProductDto(int Id, string Name, decimal Price, int CategoryId);
public record OrderDto(int Id, int CustomerId, string Status);
public record CreateOrderRequest(int CustomerId);
