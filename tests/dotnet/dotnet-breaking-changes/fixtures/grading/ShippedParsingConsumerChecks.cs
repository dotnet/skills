using System.Reflection;
using Billing;
using Xunit;

namespace Billing.GraderChecks;

// Staged only for grading. Binding method groups to exact delegate types compiles like a
// downstream consumer built against the shipped package: removing, renaming, or adding an
// optional parameter to any of these members fails to compile here even when in-repo callers
// and tests were updated to match. Reflection also rejects internalized types or methods that
// remain callable only because Billing.Tests has friend access.
public class ShippedParsingConsumerChecks
{
    [Fact]
    public void ShippedParsingTypesAndSignaturesRemainPublic()
    {
        Assert.True(typeof(AppSettingsHelper).IsPublic);
        Assert.True(typeof(ConfigReader).IsPublic);
        AssertPublicStaticMethod(
            typeof(AppSettingsHelper),
            nameof(AppSettingsHelper.ParseIntSetting),
            typeof(int),
            typeof(string),
            typeof(int));
        AssertPublicStaticMethod(
            typeof(AppSettingsHelper),
            nameof(AppSettingsHelper.ParseBoolSetting),
            typeof(bool),
            typeof(string),
            typeof(bool));
        AssertPublicStaticMethod(
            typeof(ConfigReader),
            nameof(ConfigReader.ReadInt),
            typeof(int),
            typeof(string),
            typeof(int));
        AssertPublicStaticMethod(
            typeof(ConfigReader),
            nameof(ConfigReader.ReadBool),
            typeof(bool),
            typeof(string),
            typeof(bool));
    }

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

    private static void AssertPublicStaticMethod(
        Type type,
        string name,
        Type returnType,
        params Type[] parameterTypes)
    {
        var method = type.GetMethod(
            name,
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: parameterTypes,
            modifiers: null);

        Assert.NotNull(method);
        Assert.Equal(returnType, method.ReturnType);
    }
}
