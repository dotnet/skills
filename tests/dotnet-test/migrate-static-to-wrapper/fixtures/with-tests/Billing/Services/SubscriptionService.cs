namespace Billing.Services;

public sealed class SubscriptionService
{
    public Subscription Start(string planId, int termDays)
    {
        var now = DateTime.UtcNow;

        return new Subscription
        {
            PlanId = planId,
            StartedAt = now,
            RenewsAt = now.AddDays(termDays)
        };
    }

    public bool IsActive(Subscription subscription) => DateTime.UtcNow < subscription.RenewsAt;

    public int DaysUntilRenewal(Subscription subscription)
    {
        var remaining = subscription.RenewsAt - DateTime.UtcNow;
        return remaining > TimeSpan.Zero ? (int)Math.Ceiling(remaining.TotalDays) : 0;
    }
}

public sealed class Subscription
{
    public string PlanId { get; set; } = "";

    public DateTime StartedAt { get; set; }

    public DateTime RenewsAt { get; set; }
}
