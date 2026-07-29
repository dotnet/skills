namespace OptimizingEfCoreQueries.Shared;

// Entity model shared by every benchmark in this project. Each optimization
// scenario reuses these types and the AppDbContext so the fixtures stay small
// and the "before/after" comparison is apples-to-apples.

public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Region { get; set; } = "";
    public List<Order> Orders { get; set; } = new();
    public List<Invoice> Invoices { get; set; } = new();
}

public class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public decimal Total { get; set; }
    public bool IsActive { get; set; } = true;
    public List<OrderItem> Items { get; set; } = new();
}

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public string Sku { get; set; } = "";
    public int Quantity { get; set; }
    public decimal Price { get; set; }
}

public class Product
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime LastSoldDate { get; set; }

    // Deliberately heavy columns: loading full entities drags these along even
    // when the caller only needs Id/Name/Price (see the projection scenario).
    public string Description { get; set; } = "";
    public byte[] Thumbnail { get; set; } = Array.Empty<byte>();
}

public class AuditLog
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Message { get; set; } = "";
}

public class Invoice
{
    public int Id { get; set; }
    public int Year { get; set; }
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public decimal Amount { get; set; }
}

public class Blog
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<Post> Posts { get; set; } = new();
    public List<Contributor> Contributors { get; set; } = new();
}

public class Post
{
    public int Id { get; set; }
    public int BlogId { get; set; }
    public Blog Blog { get; set; } = null!;
    public string Title { get; set; } = "";
}

public class Contributor
{
    public int Id { get; set; }
    public int BlogId { get; set; }
    public Blog Blog { get; set; } = null!;
    public string Name { get; set; } = "";
}
