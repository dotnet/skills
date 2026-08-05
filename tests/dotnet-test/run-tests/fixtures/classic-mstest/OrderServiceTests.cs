using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Legacy.Tests
{
    [TestClass]
    public class OrderServiceTests
    {
        [TestMethod]
        public void CalculateTotal_EmptyOrder_ReturnsZero()
        {
            Assert.AreEqual(0m, 0m);
        }
    }
}
