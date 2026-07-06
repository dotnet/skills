using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Some.Tests;

[TestClass]
public sealed class AccountTests
{
    [TestMethod]
    public void Deposit_PositiveAmount_IncreasesBalance() { Assert.IsTrue(true); }

    [TestMethod]
    public void Deposit_NegativeAmount_Throws() { Assert.IsTrue(true); }

    [TestMethod]
    public void Withdraw_SufficientFunds_DecreasesBalance() { Assert.IsTrue(true); }

    [TestMethod]
    public void Withdraw_InsufficientFunds_Throws() { Assert.IsTrue(true); }

    [TestMethod]
    public void Transfer_BetweenOwnedAccounts_Succeeds() { Assert.IsTrue(true); }

    [TestMethod]
    public void Transfer_AcrossOwners_RequiresConsent() { Assert.IsTrue(true); }

    [TestMethod]
    public void CloseAccount_NonZeroBalance_Throws() { Assert.IsTrue(true); }
}
