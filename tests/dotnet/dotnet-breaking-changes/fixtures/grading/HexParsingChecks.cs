using System.Reflection;
using Billing;
using Xunit;

namespace Billing.GraderChecks;

// Staged only for grading, so the requested opt-in hex behavior is checked independently of
// whatever tests the agent wrote. The delegate binding pins the shipped two-parameter
// signature that already-compiled consumers call: it does not compile when that method was
// replaced in place by one with an optional third parameter. Reflection also proves the exact
// methods remain public; Billing.Tests has friend access and could otherwise call internal APIs.
public class HexParsingChecks
{
    [Fact]
    public void ParsingTypeAndSignaturesRemainPublic()
    {
        Assert.True(typeof(AppSettingsHelper).IsPublic);
        AssertPublicStaticMethod(
            typeof(AppSettingsHelper),
            nameof(AppSettingsHelper.ParseIntSetting),
            typeof(int),
            typeof(string),
            typeof(int));
        AssertPublicStaticMethod(
            typeof(AppSettingsHelper),
            nameof(AppSettingsHelper.ParseIntSetting),
            typeof(int),
            typeof(string),
            typeof(int),
            typeof(bool));
    }

    [Fact]
    public void ShippedSignatureKeepsDecimalOnlyResults()
    {
        Func<string?, int, int> parse = AppSettingsHelper.ParseIntSetting;

        Assert.Equal(42, parse("42", 0));
        Assert.Equal(7, parse("nope", 7));
        Assert.Equal(7, parse("0x1F", 7));
        Assert.Equal(7, parse(null, 7));
    }

    [Fact]
    public void OptInParsesHexAndKeepsDecimalAndFallback()
    {
        Assert.Equal(31, AppSettingsHelper.ParseIntSetting("0x1F", 0, allowHex: true));
        Assert.Equal(255, AppSettingsHelper.ParseIntSetting("0xFF", 0, allowHex: true));
        Assert.Equal(42, AppSettingsHelper.ParseIntSetting("42", 0, allowHex: true));
        Assert.Equal(7, AppSettingsHelper.ParseIntSetting("0xZZ", 7, allowHex: true));
        Assert.Equal(7, AppSettingsHelper.ParseIntSetting(null, 7, allowHex: true));
        Assert.Equal(7, AppSettingsHelper.ParseIntSetting("0x1F", 7, allowHex: false));
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
