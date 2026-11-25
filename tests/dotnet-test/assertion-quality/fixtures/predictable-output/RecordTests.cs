using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Output.Tests;

[TestClass]
public class RecordTests
{
    private sealed record Event(string TestId, string Stream, string Text);

    // Events for this sequential run must keep their original test, stream, and order.
    private static Event[] Events() =>
        [new("Alpha", "stdout", "hello"), new("Beta", "stderr", "warning")];

    [TestMethod]
    public void Run_ForwardsOutput()
    {
        Event[] events = Events();
        Assert.AreEqual(2, events.Length);
        Assert.IsTrue(events.Any(item => item.Text == "hello"));
        Assert.IsTrue(events.Any(item => item.Text == "warning"));
    }

    [TestMethod]
    public void Run_ExactRecords()
    {
        CollectionAssert.AreEqual(
            new[] { new Event("Alpha", "stdout", "hello"), new Event("Beta", "stderr", "warning") },
            Events());
    }
}
