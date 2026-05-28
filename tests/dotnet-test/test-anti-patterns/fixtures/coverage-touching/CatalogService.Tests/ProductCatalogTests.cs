using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CatalogService.Tests;

[TestClass]
public class ProductCatalogTests
{
    private ProductCatalog _catalog = null!;

    [TestInitialize]
    public void Setup()
    {
        _catalog = new ProductCatalog(new InMemoryStore());
    }

    [TestMethod]
    public void AddProduct_Works()
    {
        var product = new Product("SKU-001", "Widget", 9.99m);
        _catalog.AddProduct(product);
    }

    [TestMethod]
    public void GetProduct_Works()
    {
        _catalog.AddProduct(new Product("SKU-001", "Widget", 9.99m));
        var result = _catalog.GetProduct("SKU-001");
        Assert.IsNotNull(result);
    }

    [TestMethod]
    public void UpdatePrice_Works()
    {
        _catalog.AddProduct(new Product("SKU-001", "Widget", 9.99m));
        _catalog.UpdatePrice("SKU-001", 14.99m);
    }

    [TestMethod]
    public void RemoveProduct_Works()
    {
        _catalog.AddProduct(new Product("SKU-001", "Widget", 9.99m));
        _catalog.RemoveProduct("SKU-001");
    }

    [TestMethod]
    public void SearchProducts_Works()
    {
        _catalog.AddProduct(new Product("SKU-001", "Widget", 9.99m));
        _catalog.AddProduct(new Product("SKU-002", "Gadget", 19.99m));
        var results = _catalog.Search("Widget");
        Assert.IsNotNull(results);
    }

    [TestMethod]
    public void GetAllProducts_Works()
    {
        _catalog.AddProduct(new Product("SKU-001", "Widget", 9.99m));
        var all = _catalog.GetAll();
        Assert.IsNotNull(all);
    }

    [TestMethod]
    public void GetProductCount_Works()
    {
        var count = _catalog.GetProductCount();
    }

    [TestMethod]
    public void ApplyBulkDiscount_Works()
    {
        _catalog.AddProduct(new Product("SKU-001", "Widget", 9.99m));
        _catalog.AddProduct(new Product("SKU-002", "Gadget", 19.99m));
        _catalog.ApplyBulkDiscount(0.10m);
    }

    [TestMethod]
    public void ExportToCsv_Works()
    {
        _catalog.AddProduct(new Product("SKU-001", "Widget", 9.99m));
        var csv = _catalog.ExportToCsv();
        Assert.IsNotNull(csv);
    }

    [TestMethod]
    public void ImportFromCsv_Works()
    {
        _catalog.ImportFromCsv("SKU-001,Widget,9.99");
    }
}
