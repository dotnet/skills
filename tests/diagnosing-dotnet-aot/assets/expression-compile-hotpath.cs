// Test asset: Expression.Compile() used in a hot path with no warning
// Expected: Skill should flag the silent 10-100x perf degradation

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace MyApp;

public class PropertyAccessorCache
{
    private readonly Dictionary<string, Func<object, object?>> _getters = new();

    // Called once per property — builds compiled expression accessors
    public void RegisterType(Type type)
    {
        foreach (var prop in type.GetProperties())
        {
            var param = Expression.Parameter(typeof(object));
            var cast = Expression.Convert(param, type);
            var access = Expression.Property(cast, prop);
            var box = Expression.Convert(access, typeof(object));
            var lambda = Expression.Lambda<Func<object, object?>>(box, param);

            // In AOT: this uses the interpreter, 10-100x slower
            _getters[prop.Name] = lambda.Compile();
        }
    }

    // Called thousands of times per request on hot path
    public object? GetValue(object obj, string propertyName)
    {
        return _getters[propertyName](obj);
    }
}

// Meanwhile, EF Core LINQ queries that use expressions are fine:
public class OrderRepository
{
    // This is safe — EF Core translates to SQL, never calls Compile()
    public IQueryable<Order> GetExpensiveOrders(DbContext db)
    {
        return db.Set<Order>().Where(o => o.Total > 100).OrderBy(o => o.Date);
    }
}

public class Order { public decimal Total { get; set; } public DateTime Date { get; set; } }
public class DbContext { public IQueryable<T> Set<T>() where T : class => throw new NotImplementedException(); }
