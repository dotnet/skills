using Microsoft.VisualStudio.TestTools.UnitTesting;
using FakeItEasy;
using Notifications;

namespace Notifications.Tests;

[TestClass]
public sealed class NotificationDispatcherTests
{
    [TestMethod]
    public void Dispatch_SmsChannel_SendsSms()
    {
        var templateEngine = A.Fake<ITemplateEngine>();
        var smsSender = A.Fake<ISmsSender>();
        var pushNotifier = A.Fake<IPushNotifier>();

        A.CallTo(() => templateEngine.Render("welcome", A<Dictionary<string, string>>.Ignored))
            .Returns("Welcome, Alice!");
        A.CallTo(() => smsSender.Send("+1234567890", "Welcome, Alice!"))
            .Returns(true);
        // Unused: push notifier is not involved in SMS dispatch
        A.CallTo(() => pushNotifier.SendPush(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored))
            .DoesNothing();

        var dispatcher = new NotificationDispatcher(templateEngine, smsSender, pushNotifier);
        var request = new NotificationRequest("user-1", "sms", "welcome", new Dictionary<string, string> { ["name"] = "Alice" });

        var result = dispatcher.Dispatch(request, "+1234567890");

        Assert.IsTrue(result);
        A.CallTo(() => smsSender.Send("+1234567890", "Welcome, Alice!")).MustHaveHappenedOnceExactly();
    }

    [TestMethod]
    public void Dispatch_PushChannel_SendsPush()
    {
        var templateEngine = A.Fake<ITemplateEngine>();
        var smsSender = A.Fake<ISmsSender>();
        var pushNotifier = A.Fake<IPushNotifier>();

        A.CallTo(() => templateEngine.Render("alert", A<Dictionary<string, string>>.Ignored))
            .Returns("Your order shipped!");
        // Unused: SMS sender is not involved in push dispatch
        A.CallTo(() => smsSender.Send(A<string>.Ignored, A<string>.Ignored))
            .Returns(true);

        var dispatcher = new NotificationDispatcher(templateEngine, smsSender, pushNotifier);
        var request = new NotificationRequest("user-2", "push", "alert", new Dictionary<string, string> { ["orderId"] = "ORD-5" });

        var result = dispatcher.Dispatch(request, "device-token-abc");

        Assert.IsTrue(result);
        A.CallTo(() => pushNotifier.SendPush("device-token-abc", "Notification", "Your order shipped!"))
            .MustHaveHappenedOnceExactly();
        // Verify SMS was never called
        A.CallTo(() => smsSender.Send(A<string>.Ignored, A<string>.Ignored)).MustNotHaveHappened();
    }

    [TestMethod]
    public void Dispatch_UnknownChannel_Throws()
    {
        var templateEngine = A.Fake<ITemplateEngine>();
        var smsSender = A.Fake<ISmsSender>();
        var pushNotifier = A.Fake<IPushNotifier>();

        A.CallTo(() => templateEngine.Render("promo", A<Dictionary<string, string>>.Ignored))
            .Returns("Sale today!");
        // These setups are never reached when channel is unknown
        A.CallTo(() => smsSender.Send(A<string>.Ignored, A<string>.Ignored)).Returns(false);
        A.CallTo(() => pushNotifier.SendPush(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).DoesNothing();

        var dispatcher = new NotificationDispatcher(templateEngine, smsSender, pushNotifier);
        var request = new NotificationRequest("user-3", "carrier-pigeon", "promo", new Dictionary<string, string>());

        Assert.ThrowsException<ArgumentException>(() => dispatcher.Dispatch(request, "info"));
    }
}
