using BlazorComponentReadiness.Validator.Assessment;
using BlazorComponentReadiness.Validator.Contracts;
using BlazorComponentReadiness.Validator.Inputs;
using BlazorComponentReadiness.Validator.IO;

namespace BlazorComponentReadiness.Validator.Cli;

internal sealed record AssessmentBindings(
    PackageRevisionBinding? Package,
    ScopedPackageContextBinding? Context);

internal static class AssessmentBindingOptions
{
    internal static AssessmentBindings Load(
        string root, string kind, InputManifest input,
        string? packageRevision, byte[]? packageFeedback,
        string? contextRevision, byte[]? contextFeedback)
    {
        var profile = ScopedComponentProfile.Load(root, input);
        if (profile is not null)
        {
            if (!string.Equals(kind, "component", StringComparison.OrdinalIgnoreCase) ||
                packageRevision is not null || packageFeedback is not null || contextRevision is null)
                throw new DeterministicValidationException(
                    "Scoped-component profile requires --package-context-revision and rejects --package-revision/--package-feedback. Context feedback uses --package-context-feedback.");
            return new AssessmentBindings(null,
                RevisionService.LoadScopedPackageContextBinding(root, Resolve(root, contextRevision), contextFeedback));
        }
        if (contextRevision is not null || contextFeedback is not null)
            throw new DeterministicValidationException(
                "--package-context-revision and --package-context-feedback require the explicit Scoped-component profile.");
        if (string.Equals(kind, "component", StringComparison.OrdinalIgnoreCase))
        {
            if (packageRevision is null)
                throw new DeterministicValidationException("Component assessment requires --package-revision.");
            return new AssessmentBindings(
                RevisionService.LoadPackageBinding(root, Path.GetFullPath(packageRevision), packageFeedback), null);
        }
        if (packageRevision is not null || packageFeedback is not null)
            throw new DeterministicValidationException("Only component assessments may specify package revision artifacts.");
        return new AssessmentBindings(null, null);
    }

    internal static InputManifest ReadRevisionInput(string root, string revision)
    {
        var directory = SafePath.ResolveExistingDirectoryUnderRoot(root, revision);
        return InputManifestService.Parse(BoundedIO.ReadAllBytes(
            SafePath.ResolveUnderRoot(directory, "input-manifest.json", true, true),
            ResourceLimits.SerializedArtifactBytes, "revision input manifest"));
    }

    private static string Resolve(string root, string path) =>
        SafePath.ResolveExistingDirectoryUnderRoot(root,
            Path.IsPathRooted(path) ? path : Path.Combine(root, path));
}
