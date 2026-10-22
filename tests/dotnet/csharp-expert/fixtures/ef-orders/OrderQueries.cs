using Microsoft.EntityFrameworkCore;

namespace EfFixture;

public sealed class OrderQueries(OrdersContext context)
{
    public async Task<IReadOnlyList<Order>> LoadAsync(CancellationToken cancellationToken)
    {
        var orders = await context.Orders.ToListAsync(cancellationToken);

        foreach (var order in orders)
        {
            await context.Entry(order).Collection(item => item.Lines).LoadAsync(cancellationToken);
        }

        return orders;
    }
}

public sealed class OrdersContext(DbContextOptions<OrdersContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
}

public sealed class Order
{
    public int Id { get; set; }
    public List<OrderLine> Lines { get; set; } = [];
}

public sealed class OrderLine
{
    public int Id { get; set; }
}
