namespace Commerce.Domain;

public readonly record struct Money(decimal Amount)
{
    public Money Add(Money other) => new(Amount + other.Amount);
}
