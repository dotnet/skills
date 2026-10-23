using Billing;
using Xunit;

namespace Billing.GraderChecks;

// Staged only for grading. Binding method groups to exact delegate types compiles like a
// downstream consumer built against the shipped package: removing, renaming, or adding an
// optional parameter to any of these members fails to compile here even when in-repo callers
// and tests were updated to match.
public class ShippedParsingConsumerChecks
{
    [Fact]
    public void ShippedParsingMembersKeepSignaturesAndResults()
    {
        Func<string?, int, int> parseInt = AppSettingsHelper.ParseIntSetting;
        Func<string?, bool, bool> parseBool = AppSettingsHelper.ParseBoolSetting;
        Func<string?, int, int> readInt = ConfigReader.ReadInt;
        Func<string?, bool, bool> readBool = ConfigReader.ReadBool;

        foreach (var read in new[] { parseInt, readInt })
        {
            Assert.Equal(42, read("42", 0));
            Assert.Equal(-5, read("-5", 0));
            Assert.Equal(7, read("nope", 7));
            Assert.Equal(7, read("", 7));
            Assert.Equal(7, read(null, 7));
        }

        foreach (var read in new[] { parseBool, readBool })
        {
            Assert.True(read("true", false));
            Assert.False(read("False", true));
            Assert.True(read("nope", true));
            Assert.False(read(null, false));
        }
    }
}
