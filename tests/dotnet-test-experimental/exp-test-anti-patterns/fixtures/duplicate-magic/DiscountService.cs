namespace DuplicateMagic;

public class DiscountService
{
    public decimal ApplyDiscount(decimal price, string tier)
    {
        return tier switch
        {
            "Gold" => price * 0.90m,
            "Silver" => price * 0.95m,
            "Bronze" => price * 0.98m,
            _ => price
        };
    }

    public decimal CalculateShipping(int weight, int ratePerUnit, int zones)
    {
        return weight * ratePerUnit * zones;
    }

    public decimal ApplyTax(decimal price, decimal taxRate)
    {
        return price * (1 + taxRate);
    }
}
