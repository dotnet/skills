using System.Text.Json;
using SkillValidator.Shared;

namespace SkillValidator.Tests;

public class ExtractJsonTests
{
    [Fact]
    public void ExtractsJsonFromMarkdownCodeBlock()
    {
        var content = "Some text\n```json\n{\"key\": \"value\"}\n```\nMore text";
        Assert.Equal("{\"key\": \"value\"}", LlmJson.ExtractJson(content));
    }

    [Fact]
    public void ExtractsJsonFromCodeBlockWithoutLanguageTag()
    {
        var content = "```\n{\"key\": \"value\"}\n```";
        Assert.Equal("{\"key\": \"value\"}", LlmJson.ExtractJson(content));
    }

    [Fact]
    public void ExtractsJsonByBraceMatchingWhenNoCodeBlock()
    {
        var content = "Here is my answer: {\"key\": \"value\"} done.";
        Assert.Equal("{\"key\": \"value\"}", LlmJson.ExtractJson(content));
    }

    [Fact]
    public void HandlesNestedBraces()
    {
        var content = "{\"outer\": {\"inner\": 1}}";
        Assert.Equal("{\"outer\": {\"inner\": 1}}", LlmJson.ExtractJson(content));
    }

    [Fact]
    public void ReturnsNullWhenNoJsonPresent()
    {
        Assert.Null(LlmJson.ExtractJson("no json here"));
    }

    [Fact]
    public void IgnoresBracesInsideStrings()
    {
        var content = "{\"key\": \"a { b } c\"}";
        Assert.Equal("{\"key\": \"a { b } c\"}", LlmJson.ExtractJson(content));
    }

    [Fact]
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
        Assert.NotNull(result);
        var parsed = JsonDocument.Parse(result).RootElement;
        Assert.Equal(4, parsed.GetProperty("overall_score").GetInt32());
    }

    [Fact]
    public void SkipsMultipleNonJsonBraceGroups()
    {
        var content = "{not json} and {also not} but {\"valid\": true} finally";
        var result = LlmJson.ExtractJson(content);
        Assert.Equal("{\"valid\": true}", result);
    }

    [Fact]
    public void SkipsBraceGroupWithInvalidEscapesAndFindsValidJsonAfterIt()
    {
        var content = "{\"a\\x\": 1 \"b\": 2} then {\"valid\": true}";
        var result = LlmJson.ExtractJson(content);
        Assert.Equal("{\"valid\": true}", result);
    }

    [Fact]
    public void ReturnsNullWhenAllBraceGroupsAreNonJson()
    {
        var content = "{not json} and {also not json}";
        Assert.Null(LlmJson.ExtractJson(content));
    }
}

public class ParseLlmJsonTests
{
    [Fact]
    public void ParsesValidJson()
    {
        var result = LlmJson.ParseLlmJson("{\"a\": 1}", "test");
        Assert.Equal(1, result.GetProperty("a").GetInt32());
    }

    [Fact]
    public void SanitizesInvalidEscapeSequences()
    {
        var raw = "{\"reasoning\": \"It\\'s good and has \\a nice \\x structure\"}";
        Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse(raw));

        var result = LlmJson.ParseLlmJson(raw, "test");
        Assert.Contains("good", result.GetProperty("reasoning").GetString());
    }

    [Fact]
    public void ThrowsWithContextForNonEscapeParseErrors()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => LlmJson.ParseLlmJson("{broken}", "test context"));
        Assert.Contains("Failed to parse test context JSON", ex.Message);
    }

    [Fact]
    public void ThrowsWithBothErrorsWhenSanitizationDoesNotHelp()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => LlmJson.ParseLlmJson("{\"key\\x\": broken}", "test"));
        Assert.Contains("even after sanitizing", ex.Message);
    }

    [Fact]
    public void IncludesJsonSnippetInErrorMessages()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => LlmJson.ParseLlmJson("{broken}", "test"));
        Assert.Contains("JSON snippet: {broken}", ex.Message);
    }
}
