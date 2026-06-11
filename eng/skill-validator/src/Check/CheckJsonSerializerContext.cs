using System.Text.Json.Serialization;

namespace SkillValidator.Check;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(CheckJsonOutput))]
internal partial class CheckJsonSerializerContext : JsonSerializerContext;
