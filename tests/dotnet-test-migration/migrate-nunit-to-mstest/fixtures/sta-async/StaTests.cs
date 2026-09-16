using System.Threading;
using NUnit.Framework;

namespace Sample.Tests;

[TestFixture]
public class StaTests
{
    [Test]
    [Apartment(ApartmentState.STA)]
    public async Task ContinuationRemainsOnSta()
    {
        await Task.Yield();
        Assert.That(Thread.CurrentThread.GetApartmentState(), Is.EqualTo(ApartmentState.STA));
    }
}
