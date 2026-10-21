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
    Require(trial.StartedAt.Kind == DateTimeKind.Utc, "StartedAt must retain UTC kind");
    Require(trial.ExpiresAt.Kind == DateTimeKind.Utc, "ExpiresAt must retain UTC kind");

    var expiry = new DateTimeOffset(trial.ExpiresAt, TimeSpan.Zero);
    clock.Now = expiry.AddTicks(-1);
    Require(manager.IsActive(trial), "Trial must be active immediately before expiry");
    clock.Now = expiry;
    Require(!manager.IsActive(trial), "Trial must be inactive exactly at expiry");
    clock.Now = expiry.AddTicks(1);
    Require(!manager.IsActive(trial), "Trial must remain inactive after expiry");
}

var steppingClock = new SteppingClock();
using var steppingServices = new ServiceCollection()
    .AddSingleton<TimeProvider>(steppingClock)
    .BuildServiceProvider();
var steppingManager = ActivatorUtilities.CreateInstance<SubscriptionManager>(steppingServices);
var steppingTrial = steppingManager.CreateTrial("stepping-user");
Require(steppingClock.Reads == 2, "Mechanical migration must preserve both trial clock reads");
Require(steppingTrial.StartedAt == steppingClock.Start.UtcDateTime, "First read must set StartedAt");
Require(steppingTrial.ExpiresAt == steppingClock.Start.AddTicks(1).UtcDateTime.AddDays(14),
    "Second read must set ExpiresAt without coalescing clock reads");
steppingManager.IsActive(steppingTrial);
Require(steppingClock.Reads == 3, "IsActive must make its own clock read");

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

sealed class SteppingClock : TimeProvider
{
    public DateTimeOffset Start { get; } = new(2042, 3, 4, 5, 6, 7, TimeSpan.Zero);
    public int Reads { get; private set; }
    public override DateTimeOffset GetUtcNow() => Start.AddTicks(Reads++);
}
