using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Contoso.Discounts.Tests
{
    public abstract class FixtureBase<TSut>
    {
        protected TSut Sut { get; private set; }

        [TestInitialize]
        public void InitializeFixture()
        {
            Sut = CreateSut();
        }

        protected abstract TSut CreateSut();
    }
}
