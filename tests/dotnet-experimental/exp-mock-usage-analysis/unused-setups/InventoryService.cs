namespace InventorySystem;

public interface IInventoryRepository
{
    int GetStock(string productId);
    void UpdateStock(string productId, int quantity);
    IReadOnlyList<string> GetLowStockProducts(int threshold);
}

public interface IAuditLogger
{
    void LogStockChange(string productId, int oldQuantity, int newQuantity, string reason);
}

public interface IEventBus
{
    void Publish(string eventName, object payload);
}

public class InventoryService
{
    private readonly IInventoryRepository _repository;
    private readonly IAuditLogger _auditLogger;
    private readonly IEventBus _eventBus;

    public InventoryService(IInventoryRepository repository, IAuditLogger auditLogger, IEventBus eventBus)
    {
        _repository = repository;
        _auditLogger = auditLogger;
        _eventBus = eventBus;
    }

    public bool Reserve(string productId, int quantity)
    {
        var current = _repository.GetStock(productId);
        if (current < quantity)
            return false;

        var newQty = current - quantity;
        _repository.UpdateStock(productId, newQty);
        _auditLogger.LogStockChange(productId, current, newQty, "Reserved");
        _eventBus.Publish("StockReserved", new { productId, quantity, remaining = newQty });
        return true;
    }

    public void Restock(string productId, int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));

        var current = _repository.GetStock(productId);
        var newQty = current + quantity;
        _repository.UpdateStock(productId, newQty);
        _auditLogger.LogStockChange(productId, current, newQty, "Restocked");
    }
}
