using AwesomeAssertions;
using Xunit;

namespace MigrationFixture;

public class LocalTests
{
    [Fact]
    public void PreservesLocalAssertions() => (2 + 2).Should().Be(4);
}
