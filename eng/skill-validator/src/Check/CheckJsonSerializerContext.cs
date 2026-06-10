using System.Text.Json.Serialization;

namespace SkillValidator;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(Check.CheckJsonOutput))]
internal partial class CheckJsonSerializerContext : JsonSerializerContext;
