using System;

namespace Contoso.Billing;

public sealed class TaxService
{
    public decimal Resolve(string region, decimal net)
    {
        var rate = 0m;
        if (region == "US")
        {
            rate = 0.07m;
        }
        else if (region == "EU")
        {
            rate = 0.20m;
        }
        else
        {
            rate = 0.15m;
        }

        return Round(net * rate);
    }

    public decimal Apply(decimal net, decimal rate)
    {
        if (rate <= 0m)
        {
            return net;
        }

        return net + Round(net * rate);
    }

    public decimal Round(decimal value)
    {
        return Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }
}
