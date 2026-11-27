using System.Data;
using Microsoft.Data.SqlClient;

namespace Contacts.SqlTests;

internal sealed class SqlContactStore(SqlConnection connection) : IContactStore
{
    public Contact? FindByNormalizedEmail(string email)
    {
        using var query = connection.CreateCommand();
        query.CommandText = "SELECT Name, Email FROM #Contacts WHERE UPPER(Email) = @email";
        query.Parameters.Add("@email", SqlDbType.NVarChar, 254).Value = email;
        using var reader = query.ExecuteReader();
        return reader.Read() ? new(reader.GetString(0), reader.GetString(1)) : null;
    }
}
