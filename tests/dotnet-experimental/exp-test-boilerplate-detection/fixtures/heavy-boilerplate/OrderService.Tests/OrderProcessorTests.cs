using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OrderService.Tests;

[TestClass]
public sealed class OrderProcessorTests
{
    [TestMethod]
    public void ProcessOrder_ValidOrder_ReturnsSuccess()
    {
        var logger = new FakeLogger();
        var emailService = new FakeEmailService();
        var inventory = new FakeInventoryService(stockLevel: 100);
        var processor = new OrderProcessor(logger, emailService, inventory);
        var order = new Order
        {
            CustomerId = "C001",
            CustomerEmail = "alice@example.com",
            Items = new List<OrderItem>
            {
                new OrderItem { ProductId = "P1", Name = "Widget", Quantity = 2, UnitPrice = 9.99m }
            }
        };

        var result = processor.Process(order);

        Assert.IsTrue(result.Success);
        Assert.AreEqual("Processed", result.Status);
    }

    [TestMethod]
    public void ProcessOrder_MultipleItems_ReturnsSuccess()
    {
        var logger = new FakeLogger();
        var emailService = new FakeEmailService();
        var inventory = new FakeInventoryService(stockLevel: 100);
        var processor = new OrderProcessor(logger, emailService, inventory);
        var order = new Order
        {
            CustomerId = "C002",
            CustomerEmail = "bob@example.com",
            Items = new List<OrderItem>
            {
                new OrderItem { ProductId = "P1", Name = "Widget", Quantity = 1, UnitPrice = 9.99m },
                new OrderItem { ProductId = "P2", Name = "Gadget", Quantity = 3, UnitPrice = 24.99m }
            }
        };

        var result = processor.Process(order);

        Assert.IsTrue(result.Success);
        Assert.AreEqual("Processed", result.Status);
    }

    [TestMethod]
    public void ProcessOrder_PremiumCustomer_AppliesDiscount()
    {
        var logger = new FakeLogger();
        var emailService = new FakeEmailService();
        var inventory = new FakeInventoryService(stockLevel: 100);
        var processor = new OrderProcessor(logger, emailService, inventory);
        var order = new Order
        {
            CustomerId = "C003",
            CustomerEmail = "carol@example.com",
            IsPremium = true,
            Items = new List<OrderItem>
            {
                new OrderItem { ProductId = "P1", Name = "Widget", Quantity = 5, UnitPrice = 10.00m }
            }
        };

        var result = processor.Process(order);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(45.00m, result.Total);
    }

    [TestMethod]
    public void ProcessOrder_EmptyItems_ReturnsFailed()
    {
        var logger = new FakeLogger();
        var emailService = new FakeEmailService();
        var inventory = new FakeInventoryService(stockLevel: 100);
        var processor = new OrderProcessor(logger, emailService, inventory);
        var order = new Order
        {
            CustomerId = "C004",
            CustomerEmail = "dave@example.com",
            Items = new List<OrderItem>()
        };

        var result = processor.Process(order);

        Assert.IsFalse(result.Success);
        Assert.AreEqual("NoItems", result.Status);
    }

    [TestMethod]
    public void ProcessOrder_NullEmail_ReturnsFailed()
    {
        var logger = new FakeLogger();
        var emailService = new FakeEmailService();
        var inventory = new FakeInventoryService(stockLevel: 100);
        var processor = new OrderProcessor(logger, emailService, inventory);
        var order = new Order
        {
            CustomerId = "C005",
            CustomerEmail = null!,
            Items = new List<OrderItem>
            {
                new OrderItem { ProductId = "P1", Name = "Widget", Quantity = 1, UnitPrice = 9.99m }
            }
        };

        var result = processor.Process(order);

        Assert.IsFalse(result.Success);
        Assert.AreEqual("InvalidEmail", result.Status);
    }

    [TestMethod]
    public void ProcessOrder_OutOfStock_ReturnsFailed()
    {
        var logger = new FakeLogger();
        var emailService = new FakeEmailService();
        var inventory = new FakeInventoryService(stockLevel: 0);
        var processor = new OrderProcessor(logger, emailService, inventory);
        var order = new Order
        {
            CustomerId = "C006",
            CustomerEmail = "eve@example.com",
            Items = new List<OrderItem>
            {
                new OrderItem { ProductId = "P1", Name = "Widget", Quantity = 10, UnitPrice = 9.99m }
            }
        };

        var result = processor.Process(order);

        Assert.IsFalse(result.Success);
        Assert.AreEqual("OutOfStock", result.Status);
    }

    [TestMethod]
    public void ProcessOrder_SendsConfirmationEmail()
    {
        var logger = new FakeLogger();
        var emailService = new FakeEmailService();
        var inventory = new FakeInventoryService(stockLevel: 100);
        var processor = new OrderProcessor(logger, emailService, inventory);
        var order = new Order
        {
            CustomerId = "C007",
            CustomerEmail = "frank@example.com",
            Items = new List<OrderItem>
            {
                new OrderItem { ProductId = "P1", Name = "Widget", Quantity = 1, UnitPrice = 9.99m }
            }
        };

        processor.Process(order);

        Assert.AreEqual(1, emailService.SentEmails.Count);
        Assert.AreEqual("frank@example.com", emailService.SentEmails[0].To);
    }

    [TestMethod]
    public void ProcessOrder_LogsOrderProcessing()
    {
        var logger = new FakeLogger();
        var emailService = new FakeEmailService();
        var inventory = new FakeInventoryService(stockLevel: 100);
        var processor = new OrderProcessor(logger, emailService, inventory);
        var order = new Order
        {
            CustomerId = "C008",
            CustomerEmail = "grace@example.com",
            Items = new List<OrderItem>
            {
                new OrderItem { ProductId = "P1", Name = "Widget", Quantity = 1, UnitPrice = 9.99m }
            }
        };

        processor.Process(order);

        Assert.IsTrue(logger.Entries.Any(e => e.Contains("Processing order")));
    }
}

[TestClass]
public sealed class OrderValidatorTests
{
    [TestMethod]
    public void Validate_ValidOrder_ReturnsTrue()
    {
        var validator = new OrderValidator();
        var order = new Order
        {
            CustomerId = "C001",
            CustomerEmail = "alice@example.com",
            Items = new List<OrderItem>
            {
                new OrderItem { ProductId = "P1", Name = "Widget", Quantity = 2, UnitPrice = 9.99m }
            }
        };

        var result = validator.Validate(order);

        Assert.IsTrue(result.IsValid);
    }

    [TestMethod]
    public void Validate_NullEmail_ReturnsFalse()
    {
        var validator = new OrderValidator();
        var order = new Order
        {
            CustomerId = "C002",
            CustomerEmail = null!,
            Items = new List<OrderItem>
            {
                new OrderItem { ProductId = "P1", Name = "Widget", Quantity = 1, UnitPrice = 9.99m }
            }
        };

        var result = validator.Validate(order);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("Email is required", result.ErrorMessage);
    }

    [TestMethod]
    public void Validate_EmptyItems_ReturnsFalse()
    {
        var validator = new OrderValidator();
        var order = new Order
        {
            CustomerId = "C003",
            CustomerEmail = "carol@example.com",
            Items = new List<OrderItem>()
        };

        var result = validator.Validate(order);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("Order must have items", result.ErrorMessage);
    }

    [TestMethod]
    public void Validate_NegativePrice_ReturnsFalse()
    {
        var validator = new OrderValidator();
        var order = new Order
        {
            CustomerId = "C004",
            CustomerEmail = "dave@example.com",
            Items = new List<OrderItem>
            {
                new OrderItem { ProductId = "P1", Name = "Widget", Quantity = 1, UnitPrice = -5.00m }
            }
        };

        var result = validator.Validate(order);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("Price must be positive", result.ErrorMessage);
    }

    [TestMethod]
    public void Validate_ZeroQuantity_ReturnsFalse()
    {
        var validator = new OrderValidator();
        var order = new Order
        {
            CustomerId = "C005",
            CustomerEmail = "eve@example.com",
            Items = new List<OrderItem>
            {
                new OrderItem { ProductId = "P1", Name = "Widget", Quantity = 0, UnitPrice = 9.99m }
            }
        };

        var result = validator.Validate(order);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("Quantity must be positive", result.ErrorMessage);
    }
}
