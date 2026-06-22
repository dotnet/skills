using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using InventorySystem;

namespace InventorySystem.Tests;

[TestClass]
public sealed class InventoryServiceTests
{
    private Mock<IInventoryRepository> _mockRepo = null!;
    private Mock<IAuditLogger> _mockAudit = null!;
    private Mock<IEventBus> _mockEventBus = null!;
    private InventoryService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockRepo = new Mock<IInventoryRepository>();
        _mockAudit = new Mock<IAuditLogger>();
        _mockEventBus = new Mock<IEventBus>();
        _service = new InventoryService(_mockRepo.Object, _mockAudit.Object, _mockEventBus.Object);
    }

    [TestMethod]
    public void Reserve_SufficientStock_ReturnsTrue()
    {
        _mockRepo.Setup(r => r.GetStock("PROD-1")).Returns(50);
        _mockRepo.Setup(r => r.UpdateStock(It.IsAny<string>(), It.IsAny<int>()));
        // Unused setup — GetLowStockProducts is never called during Reserve
        _mockRepo.Setup(r => r.GetLowStockProducts(It.IsAny<int>())).Returns(new List<string> { "PROD-1" });
        _mockAudit.Setup(a => a.LogStockChange(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()));
        _mockEventBus.Setup(e => e.Publish(It.IsAny<string>(), It.IsAny<object>()));

        var result = _service.Reserve("PROD-1", 10);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void Reserve_InsufficientStock_ReturnsFalse()
    {
        _mockRepo.Setup(r => r.GetStock("PROD-2")).Returns(5);
        // These setups are unnecessary — when stock is insufficient, UpdateStock is never called
        _mockRepo.Setup(r => r.UpdateStock(It.IsAny<string>(), It.IsAny<int>()));
        _mockAudit.Setup(a => a.LogStockChange(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()));
        _mockEventBus.Setup(e => e.Publish(It.IsAny<string>(), It.IsAny<object>()));
        // Also unused
        _mockRepo.Setup(r => r.GetLowStockProducts(10)).Returns(new List<string>());

        var result = _service.Reserve("PROD-2", 10);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void Restock_ValidQuantity_UpdatesStock()
    {
        _mockRepo.Setup(r => r.GetStock("PROD-3")).Returns(20);
        _mockRepo.Setup(r => r.UpdateStock("PROD-3", 30));
        _mockAudit.Setup(a => a.LogStockChange("PROD-3", 20, 30, "Restocked"));
        // Unused: EventBus.Publish is never called during Restock
        _mockEventBus.Setup(e => e.Publish("StockRestocked", It.IsAny<object>()));
        // Unused: GetLowStockProducts never called
        _mockRepo.Setup(r => r.GetLowStockProducts(It.IsAny<int>())).Returns(new List<string>());

        _service.Restock("PROD-3", 10);

        _mockRepo.Verify(r => r.UpdateStock("PROD-3", 30), Times.Once);
    }

    [TestMethod]
    public void Restock_ZeroQuantity_Throws()
    {
        // All these setups are unnecessary — the method throws before using any dependency
        _mockRepo.Setup(r => r.GetStock(It.IsAny<string>())).Returns(100);
        _mockRepo.Setup(r => r.UpdateStock(It.IsAny<string>(), It.IsAny<int>()));
        _mockAudit.Setup(a => a.LogStockChange(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()));
        _mockEventBus.Setup(e => e.Publish(It.IsAny<string>(), It.IsAny<object>()));

        Assert.ThrowsException<ArgumentOutOfRangeException>(() => _service.Restock("PROD-4", 0));
    }

    [TestMethod]
    public void Reserve_VerifiesExactCallSequence()
    {
        _mockRepo.Setup(r => r.GetStock("PROD-5")).Returns(100);
        _mockRepo.Setup(r => r.UpdateStock("PROD-5", 95));
        _mockAudit.Setup(a => a.LogStockChange("PROD-5", 100, 95, "Reserved"));
        _mockEventBus.Setup(e => e.Publish("StockReserved", It.IsAny<object>()));

        _service.Reserve("PROD-5", 5);

        // Over-verification: testing exact call sequence instead of outcomes
        _mockRepo.Verify(r => r.GetStock("PROD-5"), Times.Once);
        _mockRepo.Verify(r => r.UpdateStock("PROD-5", 95), Times.Once);
        _mockAudit.Verify(a => a.LogStockChange("PROD-5", 100, 95, "Reserved"), Times.Once);
        _mockEventBus.Verify(e => e.Publish("StockReserved", It.IsAny<object>()), Times.Once);
        _mockRepo.VerifyNoOtherCalls();
        _mockAudit.VerifyNoOtherCalls();
        _mockEventBus.VerifyNoOtherCalls();
    }
}
