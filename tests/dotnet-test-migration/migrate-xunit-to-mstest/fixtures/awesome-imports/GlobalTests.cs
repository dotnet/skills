using Xunit;

namespace MigrationFixture;

public class GlobalTests
{
    [Fact]
    public void PreservesGlobalAssertions() => new[] { 2, 4 }.Should().Equal(2, 4);
}
