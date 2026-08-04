namespace Banking;

public enum AccountTier
{
    Standard,
    Premium
}

public class BankAccount
{
    public BankAccount(decimal openingBalance, AccountTier tier = AccountTier.Standard)
    {
        Balance = openingBalance;
        Tier = tier;
    }

    public decimal Balance { get; private set; }

    public AccountTier Tier { get; }

    /// <summary>
    /// Adds <paramref name="amount"/> to the balance.
    /// Throws <see cref="ArgumentOutOfRangeException"/> when the amount is not positive.
    /// </summary>
    public void Deposit(decimal amount)
    {
        if (amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Deposit amount must be positive.");
        }

        Balance += amount;
    }

    /// <summary>
    /// Removes <paramref name="amount"/> from the balance.
    /// Throws <see cref="InvalidOperationException"/> with the message "Insufficient funds."
    /// when the amount exceeds the current balance.
    /// </summary>
    public void Withdraw(decimal amount)
    {
        if (amount > Balance)
        {
            throw new InvalidOperationException("Insufficient funds.");
        }

        Balance -= amount;
    }

    /// <summary>
    /// Adds interest at <paramref name="rate"/>, rounded to two decimal places.
    /// </summary>
    public void ApplyInterest(decimal rate)
    {
        Balance += decimal.Round(Balance * rate, 2);
    }
}
