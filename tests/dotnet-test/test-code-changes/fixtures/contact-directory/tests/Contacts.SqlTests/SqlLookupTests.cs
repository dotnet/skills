using Microsoft.Data.SqlClient;
using Xunit;

namespace Contacts.SqlTests;

public sealed class SqlLookupTests
{
    [Fact]
    public void Finds_contact_saved_in_sql()
    {
        using var connection = new SqlConnection(
            "Server=tcp:127.0.0.1,1;User ID=contacts;Password=local-only;Connect Timeout=1;Connect Retry Count=0");
        connection.Open();

        using var seed = connection.CreateCommand();
        seed.CommandText = """
            CREATE TABLE #Contacts (Name nvarchar(80) NOT NULL, Email nvarchar(254) NOT NULL);
            INSERT INTO #Contacts VALUES ('Jun', 'jun@example.test');
            """;
        seed.ExecuteNonQuery();

        var directory = new ContactDirectory(new SqlContactStore(connection));
        Assert.Equal(new Contact("Jun", "jun@example.test"), directory.FindByEmail("JUN@example.test"));
    }
}
