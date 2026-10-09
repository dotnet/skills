using Xunit;

[assembly: Trait("Owner", "alice")]

namespace MigrationFixture;

public abstract class SharedTests
{
    [Fact]
    public void Inherited() => Assert.Equal(4, 2 + 2);
}

[Trait("Owner", "bob")]
public class BillingTests : SharedTests { }

[Trait("Owner", "charlie")]
public class PaymentTests : SharedTests { }

public class DirectTests
{
    [Fact]
    [Trait("Owner", "bob")]
    public void ConflictingOwners() => Assert.Equal(6, 3 + 3);
}
