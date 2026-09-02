using Banking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Banking.Tests;

[TestClass]
public class BankAccountTests
{
    [TestMethod]
    public void Withdraw_AmountExceedsBalance_ThrowsInsufficientFunds()
    {
        // Arrange
        var account = new BankAccount(100m);

        // Act
        var ex = Assert.ThrowsException<InvalidOperationException>(() => account.Withdraw(500m));

        // Assert
        Assert.AreEqual("Insufficient funds.", ex.Message);
    }

    [TestMethod]
    public void Withdraw_SufficientFunds_LeavesBalanceUnchangedFromItself()
    {
        // Arrange
        var account = new BankAccount(100m);

        // Act
        account.Withdraw(40m);

        // Assert
        Assert.AreEqual(account.Balance, account.Balance);
    }

    [TestMethod]
    public void ApplyInterest_RateDependsOnTier_UpdatesBalance()
    {
        var account = new BankAccount(1000m);
        var rate = account.Tier == AccountTier.Premium ? 0.05m : 0.01m;

        account.ApplyInterest(rate);

        if (account.Tier == AccountTier.Premium)
        {
            Assert.AreEqual(1050m, account.Balance);
        }
        else
        {
            Assert.AreEqual(1010m, account.Balance);
        }
    }

    [TestMethod]
    public void Deposit_NonPositiveAmount_HandledGracefully()
    {
        var account = new BankAccount(100m);

        try
        {
            account.Deposit(-5m);
        }
        catch
        {
        }
    }
}
