using SkillValidator.Evaluate;
using SkillValidator.Shared;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SkillValidator;

[YamlStaticContext]
[YamlSerializable(typeof(SkillFrontmatter))]
[YamlSerializable(typeof(AgentFrontmatter))]
[YamlSerializable(typeof(EvalSchema.RawEvalConfig))]
[YamlSerializable(typeof(EvalSchema.RawEvalSettings))]
[YamlSerializable(typeof(EvalSchema.RawScenario))]
[YamlSerializable(typeof(EvalSchema.RawSetup))]
[YamlSerializable(typeof(EvalSchema.RawSetupFile))]
[YamlSerializable(typeof(EvalSchema.RawAssertion))]
public partial class SkillValidatorYamlContext : StaticContext
{
    /// <summary>
    /// Shared YAML deserializer using underscore naming convention.
    /// Used by EvalSchema (eval configs) and SkillDiscovery (frontmatter).
    /// </summary>
    internal static IDeserializer UnderscoredDeserializer { get; } = new StaticDeserializerBuilder(new SkillValidatorYamlContext())
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();
}
