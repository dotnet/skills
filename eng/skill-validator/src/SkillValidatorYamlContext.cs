using SkillValidator.Services;
using YamlDotNet.Serialization;

namespace SkillValidator;

[YamlStaticContext]
[YamlSerializable(typeof(EvalSchema.RawFrontmatter))]
[YamlSerializable(typeof(EvalSchema.RawEvalConfig))]
[YamlSerializable(typeof(EvalSchema.RawScenario))]
[YamlSerializable(typeof(EvalSchema.RawSetup))]
[YamlSerializable(typeof(EvalSchema.RawSetupFile))]
[YamlSerializable(typeof(EvalSchema.RawAssertion))]
public partial class SkillValidatorYamlContext : StaticContext;
