using BlazorComponentReadiness.Validator.Assessment;
using BlazorComponentReadiness.Validator.Contracts;
using BlazorComponentReadiness.Validator.Validation;
using Current = BlazorComponentReadiness.Validator.Assessment.AssessmentService;

// Keep the original regression corpus on its frozen contract, including golden report bytes.
// NormativeContractTests exercises current initialization and the requirement-basis contract.
internal static class LegacyAssessmentService
{
    public const int LegacySchemaVersion = Current.LegacySchemaVersion;

    public static ReadinessAssessment Initialize(
        string kind, string root, InputManifest input, ReadOnlySpan<byte> inputBytes,
        string? componentId, IReadOnlyList<string> overlayIds,
        PackageRevisionBinding? packageBinding = null) =>
        Current.Initialize(kind, root, input, inputBytes, componentId, overlayIds,
            packageBinding, RubricLoader.LegacyVersion);

    public static void Validate(
        string root, ReadinessAssessment assessment, ReadOnlySpan<byte> assessmentBytes,
        InputManifest input, ReadOnlySpan<byte> inputBytes, EvidenceBundle evidence,
        PackageRevisionBinding? packageBinding = null) =>
        Current.Validate(root, assessment, assessmentBytes, input, inputBytes, evidence, packageBinding);

    public static byte[] Serialize(ReadinessAssessment assessment) => Current.Serialize(assessment);
    public static ReadinessAssessment Parse(ReadOnlyMemory<byte> bytes) => Current.Parse(bytes);
}
