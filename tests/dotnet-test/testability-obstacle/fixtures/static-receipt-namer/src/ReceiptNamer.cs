namespace Receipts;

public static class ReceiptNamer
{
    public static string Create(int orderId) =>
        $"receipt-{orderId}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.txt";
}
