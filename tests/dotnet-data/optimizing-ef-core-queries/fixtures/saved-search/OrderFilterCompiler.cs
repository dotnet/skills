using System.Linq.Expressions;

namespace Contoso.Commerce.SavedSearch;

public enum SearchField
{
    Status,
    CustomerCode
}

public sealed record OrderFilter(SearchField Field, string Value);

public sealed class OrderFilterCompiler
{
    public Expression<Func<SearchOrder, bool>> Build(IReadOnlyList<OrderFilter> filters)
    {
        var order = Expression.Parameter(typeof(SearchOrder), "order");
        Expression body = Expression.Constant(true);
        foreach (var filter in filters)
        {
            var property = filter.Field switch
            {
                SearchField.Status => nameof(SearchOrder.Status),
                SearchField.CustomerCode => nameof(SearchOrder.CustomerCode),
                _ => throw new ArgumentOutOfRangeException(nameof(filters))
            };
            var condition = Expression.Equal(
                Expression.Property(order, property),
                Expression.Constant(filter.Value));
            body = Expression.AndAlso(body, condition);
        }

        return Expression.Lambda<Func<SearchOrder, bool>>(body, order);
    }
}
