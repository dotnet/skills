using Contoso.Commerce.Data;

namespace Contoso.Commerce.Services;

public sealed record HistoryCursor(DateTime CreatedAt, int Id);

internal static class HistoryQueryRules
{
    public static IQueryable<T> Page<T>(IQueryable<T> query, int pageIndex, int pageSize)
    {
        return query.Skip(pageIndex * pageSize).Take(pageSize);
    }

    public static IQueryable<Order> After(IQueryable<Order> query, HistoryCursor cursor)
    {
        return query.Where(order =>
            order.CreatedAt <= cursor.CreatedAt &&
            order.Id < cursor.Id);
    }

    public static IOrderedQueryable<Order> SupportPriority(IOrderedQueryable<Order> query)
    {
        return query.OrderByDescending(order => order.Total).ThenByDescending(order => order.Id);
    }
}
