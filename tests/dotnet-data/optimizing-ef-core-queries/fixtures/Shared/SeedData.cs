using Microsoft.EntityFrameworkCore;

namespace OptimizingEfCoreQueries.Shared;

// Deterministic seeding helpers. Volumes are picked so the "before" query is
// clearly slower than the "after" one while still seeding in well under a
// second, keeping each benchmark's [GlobalSetup] cheap.
public static class SeedData
{
    public static void SeedCustomersWithOrders(AppDbContext db, int customers, int ordersPerCustomer)
    {
        var rng = new Random(1);
        for (var c = 1; c <= customers; c++)
        {
            var customer = new Customer { Name = $"Customer {c}", Region = c % 5 == 0 ? "EU" : "US" };
            for (var o = 0; o < ordersPerCustomer; o++)
            {
                customer.Orders.Add(new Order
                {
                    CreatedAt = new DateTime(2024, 1, 1).AddHours(rng.Next(10_000)),
                    Total = rng.Next(10, 500),
                });
            }

            db.Customers.Add(customer);
        }

        db.SaveChanges();
    }

    public static void SeedProducts(AppDbContext db, int products, int categories, int descriptionChars, int thumbnailBytes)
    {
        var rng = new Random(2);
        var description = new string('x', descriptionChars);
        var thumbnail = new byte[thumbnailBytes];
        rng.NextBytes(thumbnail);

        var batch = new List<Product>(products);
        for (var i = 1; i <= products; i++)
        {
            batch.Add(new Product
            {
                CategoryId = i % categories,
                Name = $"Product {i}",
                Price = rng.Next(1, 1000),
                IsActive = true,
                LastSoldDate = new DateTime(2024, 1, 1).AddDays(-rng.Next(400)),
                Description = description,
                Thumbnail = thumbnail,
            });
        }

        db.Products.AddRange(batch);
        db.SaveChanges();
    }

    public static void SeedInvoices(AppDbContext db, int invoices, int year)
    {
        var rng = new Random(3);
        var customers = new List<Customer>();
        for (var c = 1; c <= 50; c++)
        {
            customers.Add(new Customer { Name = $"Customer {c}", Region = "US" });
        }

        db.Customers.AddRange(customers);
        db.SaveChanges();

        var batch = new List<Invoice>(invoices);
        for (var i = 1; i <= invoices; i++)
        {
            batch.Add(new Invoice
            {
                Year = year,
                CustomerId = customers[rng.Next(customers.Count)].Id,
                Amount = rng.Next(10, 10_000),
            });
        }

        db.Invoices.AddRange(batch);
        db.SaveChanges();
    }

    public static void SeedBlogs(AppDbContext db, int blogs, int postsPerBlog, int contributorsPerBlog)
    {
        for (var b = 1; b <= blogs; b++)
        {
            var blog = new Blog { Name = $"Blog {b}" };
            for (var p = 0; p < postsPerBlog; p++)
            {
                blog.Posts.Add(new Post { Title = $"Post {b}-{p}" });
            }

            for (var k = 0; k < contributorsPerBlog; k++)
            {
                blog.Contributors.Add(new Contributor { Name = $"Contributor {b}-{k}" });
            }

            db.Blogs.Add(blog);
        }

        db.SaveChanges();
    }

    public static void SeedOrders(AppDbContext db, int orders)
    {
        var rng = new Random(4);
        var customer = new Customer { Name = "Bulk", Region = "US" };
        db.Customers.Add(customer);
        db.SaveChanges();

        var batch = new List<Order>(orders);
        for (var i = 1; i <= orders; i++)
        {
            batch.Add(new Order
            {
                CustomerId = customer.Id,
                CreatedAt = new DateTime(2020, 1, 1).AddMinutes(i),
                Total = rng.Next(10, 500),
            });
        }

        db.Orders.AddRange(batch);
        db.SaveChanges();
    }
}
