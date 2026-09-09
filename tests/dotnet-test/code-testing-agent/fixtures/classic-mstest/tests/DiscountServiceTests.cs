using FizzWare.NBuilder;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Contoso.Discounts;

namespace Contoso.Discounts.Tests
{
    [TestClass]
    public class DiscountServiceTests : FixtureBase<DiscountService>
    {
        private Mock<IProductRepository> _repository;

        protected override DiscountService CreateSut()
        {
            _repository = new Mock<IProductRepository>();
            return new DiscountService(_repository.Object);
        }

        [TestMethod]
        public void Apply_ZeroPercent_ReturnsOriginalPrice()
        {
            Product product = Builder<Product>.CreateNew()
                .With(x => x.Id = 42)
                .With(x => x.Price = 25m)
                .Build();
            _repository.Setup(x => x.Get(42)).Returns(product);

            decimal result = Sut.Apply(42, 0m);

            Assert.AreEqual(25m, result);
        }
    }
}
