using Microsoft.VisualStudio.TestTools.UnitTesting;
using Output;
using static Output.Producers;

namespace Output.Tests;

[TestClass]
public class RecordTests
{
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
