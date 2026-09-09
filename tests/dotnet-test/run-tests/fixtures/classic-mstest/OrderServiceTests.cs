using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Legacy.Tests
{
    [TestClass]
    public class OrderServiceTests
    {
        [TestMethod]
        public void CalculateTotal_TwoLineItems_ReturnsSum()
        {
            decimal result = CalculateTotal(12.50m, 7.25m);

            Assert.AreEqual(19.75m, result);
        }

        private static decimal CalculateTotal(decimal first, decimal second)
        {
            return first + second;
        }
    }
}
