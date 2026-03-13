using TUnit.Core;

namespace Contoso.Notifications.Tests;

public class EmailNotificationTests
{
    [Test]
    [Category("Unit")]
    public async Task SendEmail_ValidRecipient_Succeeds() { await Task.CompletedTask; }

    [Test]
    [Category("Unit")]
    public async Task SendEmail_InvalidAddress_ThrowsException() { await Task.CompletedTask; }

    [Test]
    [Category("Integration")]
    public async Task SendEmail_SmtpServer_Delivers() { await Task.CompletedTask; }
}

public class SmsNotificationTests
{
    [Test]
    [Category("Smoke")]
    public async Task SendSms_HealthCheck() { await Task.CompletedTask; }

    [Test]
    [Category("Unit")]
    public async Task SendSms_ValidPhone_Succeeds() { await Task.CompletedTask; }
}
