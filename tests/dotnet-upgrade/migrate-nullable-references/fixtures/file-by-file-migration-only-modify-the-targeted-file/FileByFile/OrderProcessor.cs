#nullable disable

namespace FileByFile;

public class OrderProcessor
{
    private string _connectionString;

    public OrderProcessor(string connectionString)
    {
        _connectionString = connectionString;
    }

    public string ProcessOrder(string orderId)
    {
        return null;
    }
}
