using Catalog.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Catalog.Tests;

[TestClass]
public sealed class ProductCatalogTests
{
    private static ProductCatalog Seeded()
    {
        var catalog = new ProductCatalog();
        catalog.Add(new Product("WID-1", "Widget", 10.00m, 3));
        catalog.Add(new Product("GAD-2", "Gadget", 25.50m, 2));
        catalog.Add(new Product("DOO-3", "Doohickey", 4.25m, 0));
        return catalog;
    }

    // ---------------------------------------------------------------- Add

    [TestMethod]
    public void Add_NewProduct_IncreasesCount()
    {
        var catalog = new ProductCatalog();

        catalog.Add(new Product("WID-1", "Widget", 10.00m, 1));

        Assert.AreEqual(1, catalog.Count);
    }

    [TestMethod]
    public void Add_NewProduct_IsFindableBySku()
    {
        var catalog = new ProductCatalog();
        var product = new Product("WID-1", "Widget", 10.00m, 1);

        catalog.Add(product);

        Assert.AreEqual(product, catalog.Find("WID-1"));
    }

    [TestMethod]
    public void Add_NullProduct_ThrowsArgumentNullException()
    {
        var catalog = new ProductCatalog();

        Assert.ThrowsExactly<ArgumentNullException>(() => catalog.Add(null!));
    }

    [TestMethod]
    public void Add_DuplicateSku_ThrowsArgumentException()
    {
        var catalog = Seeded();

        var ex = Assert.ThrowsExactly<ArgumentException>(
            () => catalog.Add(new Product("WID-1", "Widget Clone", 1.00m, 1)));
        Assert.Contains("WID-1", ex.Message);
    }

    [TestMethod]
    public void Add_ThreeProducts_CountIsThree()
    {
        var catalog = Seeded();

        Assert.AreEqual(3, catalog.Count);
    }

    [TestMethod]
    public void Add_ProductWithZeroPrice_IsAccepted()
    {
        var catalog = new ProductCatalog();

        catalog.Add(new Product("FREE-0", "Sample", 0m, 5));

        Assert.AreEqual(0m, catalog.Find("FREE-0")!.Price);
    }

    [TestMethod]
    public void Count_AfterDuplicateAddAttempt_IsUnchanged()
    {
        var catalog = Seeded();

        try
        {
            catalog.Add(new Product("WID-1", "Widget Clone", 1.00m, 1));
        }
        catch (ArgumentException)
        {
            // expected
        }

        Assert.AreEqual(3, catalog.Count);
    }

    // --------------------------------------------------------------- Find

    [TestMethod]
    public void Find_ExistingSku_ReturnsMatchingProduct()
    {
        var catalog = Seeded();

        var product = catalog.Find("GAD-2");

        Assert.AreEqual("Gadget", product!.Name);
    }

    [TestMethod]
    public void Find_UnknownSku_ReturnsNull()
    {
        var catalog = Seeded();

        Assert.IsNull(catalog.Find("NOPE-9"));
    }

    [TestMethod]
    public void Find_EmptySku_ThrowsArgumentException()
    {
        var catalog = Seeded();

        Assert.ThrowsExactly<ArgumentException>(() => catalog.Find("   "));
    }

    [TestMethod]
    public void Find_AfterRemove_ReturnsNull()
    {
        var catalog = Seeded();

        catalog.Remove("GAD-2");

        Assert.IsNull(catalog.Find("GAD-2"));
    }

    // ------------------------------------------------------------- Remove

    [TestMethod]
    public void Remove_ExistingSku_ReturnsTrue()
    {
        var catalog = Seeded();

        Assert.IsTrue(catalog.Remove("WID-1"));
    }

    [TestMethod]
    public void Remove_ExistingSku_DecreasesCount()
    {
        var catalog = Seeded();

        catalog.Remove("WID-1");

        Assert.AreEqual(2, catalog.Count);
    }

    [TestMethod]
    public void Remove_UnknownSku_ReturnsFalse()
    {
        var catalog = Seeded();

        Assert.IsFalse(catalog.Remove("NOPE-9"));
    }

    [TestMethod]
    public void Remove_UnknownSku_LeavesCountUnchanged()
    {
        var catalog = Seeded();

        catalog.Remove("NOPE-9");

        Assert.AreEqual(3, catalog.Count);
    }

    // -------------------------------------------------------------- Count

    [TestMethod]
    public void Count_EmptyCatalog_IsZero()
    {
        Assert.AreEqual(0, new ProductCatalog().Count);
    }

    [TestMethod]
    public void Count_AfterClear_IsZero()
    {
        var catalog = Seeded();

        catalog.Clear();

        Assert.AreEqual(0, catalog.Count);
    }

    // --------------------------------------------------------- TotalValue

    [TestMethod]
    public void TotalValue_EmptyCatalog_IsZero()
    {
        Assert.AreEqual(0m, new ProductCatalog().TotalValue());
    }

    [TestMethod]
    public void TotalValue_SingleProduct_IsPriceTimesQuantity()
    {
        var catalog = new ProductCatalog();
        catalog.Add(new Product("WID-1", "Widget", 10.00m, 3));

        Assert.AreEqual(30.00m, catalog.TotalValue());
    }

    [TestMethod]
    public void TotalValue_MultipleProducts_SumsAllLines()
    {
        var catalog = Seeded();

        Assert.AreEqual(81.00m, catalog.TotalValue());
    }

    [TestMethod]
    public void TotalValue_AfterRemove_ExcludesRemovedLine()
    {
        var catalog = Seeded();

        catalog.Remove("GAD-2");

        Assert.AreEqual(30.00m, catalog.TotalValue());
    }

    [TestMethod]
    public void TotalValue_AfterDiscount_ReflectsNewPrices()
    {
        var catalog = Seeded();

        catalog.ApplyDiscount(50m);

        Assert.AreEqual(40.50m, catalog.TotalValue());
    }

    // ------------------------------------------------------------- Search

    [TestMethod]
    public void Search_MatchingTerm_ReturnsMatches()
    {
        var catalog = Seeded();

        var results = catalog.Search("Widget");

        Assert.HasCount(1, results);
    }

    [TestMethod]
    public void Search_NoMatch_ReturnsEmptyList()
    {
        var catalog = Seeded();

        Assert.IsEmpty(catalog.Search("Sprocket"));
    }

    [TestMethod]
    public void Search_IsCaseInsensitive()
    {
        var catalog = Seeded();

        var results = catalog.Search("gadget");

        Assert.AreEqual("GAD-2", results[0].Sku);
    }

    [TestMethod]
    public void Search_NullTerm_ThrowsArgumentNullException()
    {
        var catalog = Seeded();

        Assert.ThrowsExactly<ArgumentNullException>(() => catalog.Search(null!));
    }

    [TestMethod]
    public void Search_WhitespaceTerm_ThrowsArgumentException()
    {
        var catalog = Seeded();

        Assert.ThrowsExactly<ArgumentException>(() => catalog.Search("  "));
    }

    [TestMethod]
    public void Search_PartialSkuMatch_ReturnsProduct()
    {
        var catalog = Seeded();

        var results = catalog.Search("DOO");

        Assert.AreEqual("Doohickey", results[0].Name);
    }

    // ------------------------------------------------------------ Restock

    [TestMethod]
    public void Restock_KnownSku_IncreasesQuantity()
    {
        var catalog = Seeded();

        catalog.Restock("WID-1", 4);

        Assert.AreEqual(7, catalog.Find("WID-1")!.Quantity);
    }

    [TestMethod]
    public void Restock_UnknownSku_ThrowsKeyNotFound()
    {
        var catalog = Seeded();

        Assert.ThrowsExactly<KeyNotFoundException>(() => catalog.Restock("NOPE-9", 1));
    }

    [TestMethod]
    public void Restock_NegativeQuantity_ThrowsArgumentOutOfRange()
    {
        var catalog = Seeded();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => catalog.Restock("WID-1", -1));
    }

    [TestMethod]
    public void Restock_ZeroQuantity_ThrowsArgumentOutOfRange()
    {
        var catalog = Seeded();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => catalog.Restock("WID-1", 0));
    }

    [TestMethod]
    public void Restock_TwiceInARow_AccumulatesQuantity()
    {
        var catalog = Seeded();

        catalog.Restock("WID-1", 2);
        catalog.Restock("WID-1", 5);

        Assert.AreEqual(10, catalog.Find("WID-1")!.Quantity);
    }

    // ------------------------------------------------------ ApplyDiscount

    [TestMethod]
    public void ApplyDiscount_TenPercent_ReducesEveryPrice()
    {
        var catalog = Seeded();

        catalog.ApplyDiscount(10m);

        Assert.AreEqual(9.00m, catalog.Find("WID-1")!.Price);
    }

    [TestMethod]
    public void ApplyDiscount_Zero_LeavesPricesUnchanged()
    {
        var catalog = Seeded();

        catalog.ApplyDiscount(0m);

        Assert.AreEqual(10.00m, catalog.Find("WID-1")!.Price);
    }

    [TestMethod]
    public void ApplyDiscount_Negative_ThrowsArgumentOutOfRange()
    {
        var catalog = Seeded();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => catalog.ApplyDiscount(-5m));
    }

    [TestMethod]
    public void ApplyDiscount_OverOneHundred_ThrowsArgumentOutOfRange()
    {
        var catalog = Seeded();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => catalog.ApplyDiscount(120m));
    }

    [TestMethod]
    public void ApplyDiscount_RoundsToTwoDecimals()
    {
        var catalog = Seeded();

        catalog.ApplyDiscount(33m);

        Assert.AreEqual(2.85m, catalog.Find("DOO-3")!.Price);
    }

    // -------------------------------------------------------------- Clear

    [TestMethod]
    public void Clear_PopulatedCatalog_RemovesEveryProduct()
    {
        var catalog = Seeded();

        catalog.Clear();

        Assert.IsEmpty(catalog.Search("Widget"));
    }

    [TestMethod]
    public void Clear_EmptyCatalog_DoesNotThrow()
    {
        var catalog = new ProductCatalog();

        catalog.Clear();

        Assert.AreEqual(0, catalog.Count);
    }

    // ---------------------------------------------------------- IsInStock

    [TestMethod]
    public void IsInStock_PositiveQuantity_ReturnsTrue()
    {
        var catalog = Seeded();

        Assert.IsTrue(catalog.IsInStock("WID-1"));
    }

    [TestMethod]
    public void IsInStock_ZeroQuantity_ReturnsFalse()
    {
        var catalog = Seeded();

        Assert.IsFalse(catalog.IsInStock("DOO-3"));
    }

    [TestMethod]
    public void IsInStock_UnknownSku_ReturnsFalse()
    {
        var catalog = Seeded();

        Assert.IsFalse(catalog.IsInStock("NOPE-9"));
    }

    [TestMethod]
    public void IsInStock_AfterRestockFromZero_ReturnsTrue()
    {
        var catalog = Seeded();

        catalog.Restock("DOO-3", 6);

        Assert.IsTrue(catalog.IsInStock("DOO-3"));
    }

    // -------------------------------------------- null-check-only checks

    [TestMethod]
    public void Find_KnownSku_ReturnsSomething()
    {
        var catalog = Seeded();

        var product = catalog.Find("WID-1");

        Assert.IsNotNull(product);
    }

    [TestMethod]
    public void Search_KnownTerm_ReturnsSomething()
    {
        var catalog = Seeded();

        var results = catalog.Search("Widget");

        Assert.IsNotNull(results);
    }

    [TestMethod]
    public void Search_EmptyResultSet_ReturnsList()
    {
        var catalog = Seeded();

        var results = catalog.Search("Sprocket");

        Assert.IsNotNull(results);
    }

    [TestMethod]
    public void Add_ThenFind_ReturnsSomething()
    {
        var catalog = new ProductCatalog();
        catalog.Add(new Product("NEW-1", "Newbie", 1.00m, 1));

        var product = catalog.Find("NEW-1");

        Assert.IsNotNull(product);
    }

    [TestMethod]
    public void Restock_ThenFind_ReturnsSomething()
    {
        var catalog = Seeded();
        catalog.Restock("WID-1", 1);

        var product = catalog.Find("WID-1");

        Assert.IsNotNull(product);
    }

    [TestMethod]
    public void ApplyDiscount_ThenFind_ReturnsSomething()
    {
        var catalog = Seeded();
        catalog.ApplyDiscount(5m);

        var product = catalog.Find("GAD-2");

        Assert.IsNotNull(product);
    }

    [TestMethod]
    public void Clear_ThenSearch_ReturnsList()
    {
        var catalog = Seeded();
        catalog.Clear();

        var results = catalog.Search("Widget");

        Assert.IsNotNull(results);
    }

    [TestMethod]
    public void Remove_ThenSearch_ReturnsList()
    {
        var catalog = Seeded();
        catalog.Remove("WID-1");

        var results = catalog.Search("Gadget");

        Assert.IsNotNull(results);
    }

    [TestMethod]
    public void TotalValue_ThenFind_ReturnsSomething()
    {
        var catalog = Seeded();
        _ = catalog.TotalValue();

        var product = catalog.Find("DOO-3");

        Assert.IsNotNull(product);
    }

    [TestMethod]
    public void Find_AfterMultipleAdds_ReturnsSomething()
    {
        var catalog = Seeded();
        catalog.Add(new Product("EXT-4", "Extra", 2.00m, 1));

        var product = catalog.Find("EXT-4");

        Assert.IsNotNull(product);
    }

    [TestMethod]
    public void Search_AfterRestock_ReturnsList()
    {
        var catalog = Seeded();
        catalog.Restock("GAD-2", 3);

        var results = catalog.Search("Gadget");

        Assert.IsNotNull(results);
    }

    // ------------------------------------------------ assertion-free runs

    [TestMethod]
    public void Add_ManyProducts_Runs()
    {
        var catalog = new ProductCatalog();
        for (var i = 0; i < 20; i++)
        {
            catalog.Add(new Product($"BULK-{i}", $"Bulk {i}", i, i));
        }
    }

    [TestMethod]
    public void Clear_Runs()
    {
        var catalog = Seeded();
        catalog.Clear();
    }

    [TestMethod]
    public void Restock_Runs()
    {
        var catalog = Seeded();
        catalog.Restock("WID-1", 3);
    }

    [TestMethod]
    public void ApplyDiscount_Runs()
    {
        var catalog = Seeded();
        catalog.ApplyDiscount(15m);
    }

    [TestMethod]
    public void Remove_Runs()
    {
        var catalog = Seeded();
        catalog.Remove("GAD-2");
    }

    [TestMethod]
    public void Search_Runs()
    {
        var catalog = Seeded();
        _ = catalog.Search("Widget");
    }

    [TestMethod]
    public void TotalValue_Runs()
    {
        var catalog = Seeded();
        _ = catalog.TotalValue();
    }
}
