using System.Text.Json;
using SkillValidator.Shared;

namespace SkillValidator.Tests;

[TestClass]
public class ExtractJsonTests
{
    [TestMethod]
    public void ExtractsJsonFromMarkdownCodeBlock()
    {
        var content = "Some text\n```json\n{\"key\": \"value\"}\n```\nMore text";
        Assert.AreEqual("{\"key\": \"value\"}", LlmJson.ExtractJson(content));
    }

    [TestMethod]
    public void ExtractsJsonFromCodeBlockWithoutLanguageTag()
    {
        var content = "```\n{\"key\": \"value\"}\n```";
        Assert.AreEqual("{\"key\": \"value\"}", LlmJson.ExtractJson(content));
    }

    [TestMethod]
    public void ExtractsJsonByBraceMatchingWhenNoCodeBlock()
    {
        var content = "Here is my answer: {\"key\": \"value\"} done.";
        Assert.AreEqual("{\"key\": \"value\"}", LlmJson.ExtractJson(content));
    }

    [TestMethod]
    public void HandlesNestedBraces()
    {
        var content = "{\"outer\": {\"inner\": 1}}";
        Assert.AreEqual("{\"outer\": {\"inner\": 1}}", LlmJson.ExtractJson(content));
    }

    [TestMethod]
    public void ReturnsNullWhenNoJsonPresent()
    {
        Assert.IsNull(LlmJson.ExtractJson("no json here"));
    }

    [TestMethod]
    public void IgnoresBracesInsideStrings()
    {
        var content = "{\"key\": \"a { b } c\"}";
        Assert.AreEqual("{\"key\": \"a { b } c\"}", LlmJson.ExtractJson(content));
    }

    [TestMethod]
    public void SkipsNonJsonBraceGroupsLikeCSharpCode()
    {
        var content = """
            Here is the code:
            {
                [LibraryImport("compresslib", EntryPoint = "compress")]
                internal static partial int Compress(ReadOnlySpan<byte> input);
            }

            And here is my evaluation:
            {"rubric_scores": [{"criterion": "Quality", "score": 4, "reasoning": "Good"}], "overall_score": 4, "overall_reasoning": "Solid work"}
            """;
        var result = LlmJson.ExtractJson(content);
        Assert.IsNotNull(result);
        var parsed = JsonDocument.Parse(result).RootElement;
        Assert.AreEqual(4, parsed.GetProperty("overall_score").GetInt32());
    }

    [TestMethod]
    public void SkipsMultipleNonJsonBraceGroups()
    {
        var content = "{not json} and {also not} but {\"valid\": true} finally";
        var result = LlmJson.ExtractJson(content);
        Assert.AreEqual("{\"valid\": true}", result);
    }

    [TestMethod]
    public void SkipsBraceGroupWithInvalidEscapesAndFindsValidJsonAfterIt()
    {
        var content = "{\"a\\x\": 1 \"b\": 2} then {\"valid\": true}";
        var result = LlmJson.ExtractJson(content);
        Assert.AreEqual("{\"valid\": true}", result);
    }

    [TestMethod]
    public void ReturnsNullWhenAllBraceGroupsAreNonJson()
    {
        var content = "{not json} and {also not json}";
        Assert.IsNull(LlmJson.ExtractJson(content));
    }
}

[TestClass]
public class ParseLlmJsonTests
{
    [TestMethod]
    public void ParsesValidJson()
    {
        var result = LlmJson.ParseLlmJson("{\"a\": 1}", "test");
        Assert.AreEqual(1, result.GetProperty("a").GetInt32());
    }

    [TestMethod]
    public void SanitizesInvalidEscapeSequences()
    {
        var raw = "{\"reasoning\": \"It\\'s good and has \\a nice \\x structure\"}";
        Assert.Throws<JsonException>(() => JsonDocument.Parse(raw));

        var result = LlmJson.ParseLlmJson(raw, "test");
        Assert.Contains("good", result.GetProperty("reasoning").GetString()!);
    }

    [TestMethod]
    public void ThrowsWithContextForNonEscapeParseErrors()
    {
        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => LlmJson.ParseLlmJson("{broken}", "test context"));
        Assert.Contains("Failed to parse test context JSON", ex.Message);
    }

    [TestMethod]
    public void ThrowsWithBothErrorsWhenSanitizationDoesNotHelp()
    {
        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => LlmJson.ParseLlmJson("{\"key\\x\": broken}", "test"));
        Assert.Contains("even after sanitizing", ex.Message);
    }

    [TestMethod]
    public void IncludesJsonSnippetInErrorMessages()
    {
        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => LlmJson.ParseLlmJson("{broken}", "test"));
        Assert.Contains("JSON snippet: {broken}", ex.Message);
    }
}
