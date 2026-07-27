using Billing.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Billing.Tests;

[TestClass]
public sealed class SubscriptionServiceTests
{
    [TestMethod]
    public void Start_RenewsAtIsStartPlusTerm()
    {
        var service = new SubscriptionService();

        var subscription = service.Start("pro", 30);

        Assert.AreEqual(30d, (subscription.RenewsAt - subscription.StartedAt).TotalDays, 0.001);
    }

    [TestMethod]
    public void IsActive_ReturnsFalse_WhenRenewalDateHasPassed()
    {
        var service = new SubscriptionService();
        var subscription = new Subscription
        {
            PlanId = "pro",
            StartedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            RenewsAt = new DateTime(2020, 2, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        Assert.IsFalse(service.IsActive(subscription));
    }

    [TestMethod]
    public void DaysUntilRenewal_ReturnsZero_WhenRenewalDateHasPassed()
    {
        var service = new SubscriptionService();
        var subscription = new Subscription
        {
            PlanId = "pro",
            StartedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            RenewsAt = new DateTime(2020, 2, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        Assert.AreEqual(0, service.DaysUntilRenewal(subscription));
    }
}
