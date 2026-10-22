using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Contoso.Commerce.SavedSearch;

internal static class OrderQueryPolicies
{
    public static Expression<Func<SearchOrder, bool>> CustomerActivity(string customerCode)
    {
        return order => order.CustomerCode == EF.Constant(customerCode);
    }

    public static Expression<Func<SearchOrder, bool>> Imported()
    {
        return order => order.Status == EF.Parameter("Imported");
    }
}
