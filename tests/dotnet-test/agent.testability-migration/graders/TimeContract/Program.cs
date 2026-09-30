using FullPipeline.Services;
using Microsoft.Extensions.DependencyInjection;

var clock = new ControlledClock();
using var services = new ServiceCollection()
    .AddSingleton<TimeProvider>(clock)
    .BuildServiceProvider();
var manager = ActivatorUtilities.CreateInstance<SubscriptionManager>(services);

foreach (var instant in new[]
{
    new DateTimeOffset(2041, 2, 27, 23, 30, 0, TimeSpan.FromHours(5)),
    new DateTimeOffset(2001, 12, 31, 1, 0, 0, TimeSpan.FromHours(-7)),
})
{
    clock.Now = instant;
    var trial = manager.CreateTrial("contract-user");
    Require(trial.UserId == "contract-user", "UserId must be preserved");
    Require(trial.Plan == "trial", "Plan must remain trial");
    Require(trial.StartedAt == instant.UtcDateTime, "StartedAt must use the injected UTC clock");
    Require(trial.ExpiresAt == instant.UtcDateTime.AddDays(14), "Expiry must be fourteen days after start");

    var expiry = new DateTimeOffset(trial.ExpiresAt, TimeSpan.Zero);
    clock.Now = expiry.AddTicks(-1);
    Require(manager.IsActive(trial), "Trial must be active immediately before expiry");
    clock.Now = expiry;
    Require(!manager.IsActive(trial), "Trial must be inactive exactly at expiry");
    clock.Now = expiry.AddTicks(1);
    Require(!manager.IsActive(trial), "Trial must remain inactive after expiry");
}

Console.WriteLine("Injected-clock trial dates and expiry boundaries passed.");

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

sealed class ControlledClock : TimeProvider
{
    public DateTimeOffset Now { get; set; }
    public override DateTimeOffset GetUtcNow() => Now;
}
