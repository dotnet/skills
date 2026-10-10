using Xunit;

[assembly: Trait("Owner", "alice")]

namespace MigrationFixture;

public class AssemblyOwnerTests
{
    [Fact]
    public void InheritsAlice() => Assert.Equal(4, 2 + 2);
}

[Trait("Owner", "alice")]
public class ClassOwnerTests
{
    [Fact]
    [Trait("Owner", "alice")]
    public void DeduplicatesAlice() => Assert.Equal(6, 3 + 3);

    [Fact]
    public void InheritsClassAlice() => Assert.Equal(8, 4 + 4);
}
