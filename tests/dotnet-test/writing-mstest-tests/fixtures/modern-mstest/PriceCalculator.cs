namespace Contoso.Pricing;

public sealed class PriceCalculator
{
    public decimal CalculateTotal(decimal unitPrice, int quantity, decimal taxRate)
    {
        if (quantity < 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity cannot be negative.");
        if (unitPrice < 0)
            throw new ArgumentOutOfRangeException(nameof(unitPrice), "Unit price cannot be negative.");
        if (taxRate < 0 || taxRate > 1)
            throw new ArgumentOutOfRangeException(nameof(taxRate), "Tax rate must be between 0 and 1.");

        var subtotal = unitPrice * quantity;
        return subtotal + (subtotal * taxRate);
    }

    public decimal ApplyDiscount(decimal price, decimal discountPercent)
    {
        if (discountPercent < 0 || discountPercent > 100)
            throw new ArgumentOutOfRangeException(nameof(discountPercent));

        return price * (1 - discountPercent / 100);
    }

    public string FormatPrice(decimal price, string currencySymbol = "$")
    {
        return $"{currencySymbol}{price:F2}";
    }
}
