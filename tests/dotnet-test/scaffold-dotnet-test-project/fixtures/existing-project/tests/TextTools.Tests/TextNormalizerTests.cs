using TextTools;
using Xunit;

namespace TextTools.Tests;

public sealed class TextNormalizerTests
{
    [Fact]
    public void CollapseSpaces_RemovesRepeatedSpaces()
    {
        Assert.Equal("hello world", TextNormalizer.CollapseSpaces("hello   world"));
    }
}
