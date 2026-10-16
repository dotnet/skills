using System.Data;
using Dapper;

namespace Contoso.Catalog;

public sealed class DapperRepository(IDbConnection connection)
{
    public Task<IEnumerable<ProductRow>> SearchProductsAsync(string term) =>
        connection.QueryAsync<ProductRow>(
            "SELECT Id, Name, Price FROM Products WHERE Name LIKE '%' + @term + '%'",
            new { term });
}

public sealed record ProductRow(int Id, string Name, decimal Price);
