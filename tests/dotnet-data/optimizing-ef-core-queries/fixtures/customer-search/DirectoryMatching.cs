using System.Linq.Expressions;

namespace Contoso.Commerce.Search;

internal static class DirectoryMatching
{
    public static Expression<Func<DirectoryCustomer, bool>> Email(string email)
    {
        var value = email.Trim().ToLowerInvariant();
        return customer => customer.Email.ToLower() == value;
    }

    public static Expression<Func<CatalogProduct, bool>> CatalogText(string term)
    {
        return product => product.Name.Contains(term);
    }

    public static Expression<Func<DirectoryCustomer, bool>> NameHint(string prefix)
    {
        return customer => customer.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    public static Expression<Func<DirectoryCustomer, bool>> NamePrefix(string prefix)
    {
        return customer => customer.Name.StartsWith(prefix);
    }

    public static Expression<Func<DirectoryCustomer, bool>> Regions(IReadOnlyCollection<string> regions)
    {
        return customer => regions.Contains(customer.Region);
    }
}
