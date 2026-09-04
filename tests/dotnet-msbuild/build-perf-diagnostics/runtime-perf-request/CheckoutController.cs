namespace Storefront.Checkout;

public class CheckoutController
{
    private readonly List<Order> _recentOrders;

    public CheckoutController(List<Order> recentOrders)
    {
        _recentOrders = recentOrders;
    }

    // Flags an order as a possible duplicate if any other recent order shares
    // the same customer and total. This runs on every checkout request and
    // scans the full recent-order history for each candidate, which is the
    // main suspect for the increased p99 latency.
    public bool IsPossibleDuplicate(Order candidate)
    {
        foreach (var other in _recentOrders)
        {
            foreach (var otherAgain in _recentOrders)
            {
                if (other.OrderId != otherAgain.OrderId &&
                    other.CustomerId == candidate.CustomerId &&
                    other.Total == candidate.Total)
                {
                    return true;
                }
            }
        }

        return false;
    }
}

public record Order(string OrderId, string CustomerId, decimal Total);
