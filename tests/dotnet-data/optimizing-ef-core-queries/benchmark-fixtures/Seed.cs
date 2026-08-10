using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Contoso.Sales.Operations;

namespace Contoso.Sales.Operations.Benchmarks;

// Deterministic seeding, one slice per benchmarked method. Each method runs
// against its own freshly-seeded in-memory database, so the mutating discount job
// cannot disturb the read benchmarks. Volumes are chosen so the original body is
// clearly slower than a correct rewrite while still seeding in a second or two.
//
// Customer rows are always inserted before the orders/invoices that reference
// them because the SQLite provider enforces foreign keys.
internal static class Seed
{
    public const int TargetCustomerId = 1;
    public const int TargetCategoryId = 1;

    // Total threshold used by the order-list benchmarks below.
    public const decimal MinTotal = 400m;

    // GetCustomerSales: many customers, each with orders spread across a few years,
    // so the per-customer query loop (N+1) pays one round-trip per customer and the
    // year filter selects a subset.
    public static void CustomerSales(SqliteConnection connection)
    {
        using var db = Timing.NewContext(connection);
        db.Database.EnsureCreated();

        var rng = new Random(1);
        for (var c = 1; c <= 300; c++)
        {
            db.Customers.Add(new Customer { Name = $"Customer {c}", Region = c % 5 == 0 ? "EU" : "US" });
        }

        db.SaveChanges();

        var orders = new List<Order>(300 * 12);
        for (var c = 1; c <= 300; c++)
        {
            for (var o = 0; o < 12; o++)
            {
                var year = 2023 + (o % 3); // 2023, 2024, 2025
                orders.Add(new Order
                {
                    CustomerId = c,
                    CreatedAt = new DateTime(year, 1, 1).AddHours(rng.Next(8000)),
                    Total = rng.Next(10, 500),
                    Status = "Complete",
                });
            }
        }

        db.Orders.AddRange(orders);
        db.SaveChanges();
    }

    // GetCustomerDetail: one customer whose orders (with lines) and invoices are two
    // independent collections, so a single Include query multiplies them together.
    public static void CustomerDetail(SqliteConnection connection)
    {
        using var db = Timing.NewContext(connection);
        db.Database.EnsureCreated();

        var rng = new Random(2);
        var customer = new Customer { Name = "Customer 1", Region = "US" };
        for (var o = 0; o < 150; o++)
        {
            var order = new Order
            {
                CreatedAt = new DateTime(2024, 1, 1).AddHours(o),
                Total = rng.Next(10, 500),
                Status = "Complete",
            };
            for (var l = 0; l < 2; l++)
            {
                order.Lines.Add(new OrderLine { Sku = $"SKU-{o}-{l}", Quantity = rng.Next(1, 5), UnitPrice = rng.Next(1, 100) });
            }

            customer.Orders.Add(order);
        }

        for (var i = 0; i < 100; i++)
        {
            customer.Invoices.Add(new Invoice { Amount = rng.Next(10, 1000), Paid = i % 2 == 0 });
        }

        db.Customers.Add(customer);
        db.SaveChanges();
    }

    // HasOrders: the target customer's many orders are inserted FIRST so an
    // existence check stops at the first row while a full count must scan them all.
    public static void HasOrders(SqliteConnection connection)
    {
        using var db = Timing.NewContext(connection);
        db.Database.EnsureCreated();

        var rng = new Random(3);
        for (var c = 1; c <= 50; c++)
        {
            db.Customers.Add(new Customer { Name = $"Customer {c}", Region = "US" });
        }

        db.SaveChanges();

        var orders = new List<Order>(25000);
        for (var o = 0; o < 20000; o++) // target customer first
        {
            orders.Add(new Order { CustomerId = TargetCustomerId, CreatedAt = new DateTime(2024, 1, 1).AddMinutes(o), Total = rng.Next(10, 500), Status = "Complete" });
        }

        for (var c = 2; c <= 50; c++)
        {
            for (var o = 0; o < 100; o++)
            {
                orders.Add(new Order { CustomerId = c, CreatedAt = new DateTime(2024, 1, 1).AddMinutes(o), Total = rng.Next(10, 500), Status = "Complete" });
            }
        }

        db.Orders.AddRange(orders);
        db.SaveChanges();
    }

    // GetUnpaidInvoices: a large read-only export whose rows the baseline tracks.
    public static void UnpaidInvoices(SqliteConnection connection)
    {
        using var db = Timing.NewContext(connection);
        db.Database.EnsureCreated();

        var rng = new Random(4);
        for (var c = 1; c <= 100; c++)
        {
            db.Customers.Add(new Customer { Name = $"Customer {c}", Region = "US" });
        }

        db.SaveChanges();

        var invoices = new List<Invoice>(15000);
        for (var i = 0; i < 15000; i++)
        {
            invoices.Add(new Invoice { CustomerId = rng.Next(1, 101), Amount = rng.Next(10, 10000), Paid = false });
        }

        db.Invoices.AddRange(invoices);
        db.SaveChanges();
    }

    // Orders slice shared by the order-list benchmarks (GetOrdersOverTotal,
    // GetPendingOrders). A large table with sequential ids, a spread of totals, unique
    // timestamps and a minority of "Pending" rows, so a query that pulls the whole table
    // to the app tier to filter/sort is clearly slower than one that lets the
    // database filter and order.
    public static void Orders(SqliteConnection connection)
    {
        using var db = Timing.NewContext(connection);
        db.Database.EnsureCreated();

        var rng = new Random(6);
        for (var c = 1; c <= 100; c++)
        {
            db.Customers.Add(new Customer { Name = $"Customer {c}", Region = "US" });
        }

        db.SaveChanges();

        var start = new DateTime(2024, 1, 1);
        var orders = new List<Order>(15000);
        for (var i = 0; i < 15000; i++)
        {
            orders.Add(new Order
            {
                CustomerId = 1 + (i % 100),
                CreatedAt = start.AddMinutes(i), // unique per row
                Total = rng.Next(10, 500),
                Status = i % 10 == 0 ? "Pending" : "Complete",
            });
        }

        db.Orders.AddRange(orders);
        db.SaveChanges();
    }

    // Products slice shared by the product-grid, discount and count benchmarks. The
    // wide Description/Image columns make loading whole entities expensive when only
    // a few fields are needed. Returns the category the read/count/discount target.
    public static void Products(SqliteConnection connection)
    {
        using var db = Timing.NewContext(connection);
        db.Database.EnsureCreated();

        var rng = new Random(5);
        var description = new string('x', 1500);

        var products = new List<Product>(5000);
        for (var i = 1; i <= 5000; i++)
        {
            var image = new byte[2000];
            rng.NextBytes(image);
            products.Add(new Product
            {
                CategoryId = i <= 1500 ? TargetCategoryId : 2 + (i % 8),
                Name = $"Product {i}",
                Price = rng.Next(1, 1000),
                Discontinued = i % 50 == 0,
                UpdatedAt = new DateTime(2024, 1, 1).AddMinutes(i),
                Description = description,
                Image = image,
            });
        }

        db.Products.AddRange(products);
        db.SaveChanges();
    }
}
