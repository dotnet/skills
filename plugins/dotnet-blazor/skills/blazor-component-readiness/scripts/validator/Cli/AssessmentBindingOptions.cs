using BlazorComponentReadiness.Validator.Assessment;
using BlazorComponentReadiness.Validator.Contracts;
using BlazorComponentReadiness.Validator.Inputs;
using BlazorComponentReadiness.Validator.IO;
using BlazorComponentReadiness.Validator.Validation;

namespace BlazorComponentReadiness.Validator.Cli;

internal sealed record AssessmentBindings(
    PackageRevisionBinding? Package,
    ScopedPackageContextBinding? Context);

internal static class AssessmentBindingOptions
{
    internal static void RejectRetiredOptions(CommandOptions options)
    {
        if (options.Optional("--package-context-revision") is not null ||
            options.Optional("--package-context-feedback") is not null)
        {
            throw new DeterministicValidationException(
                "Scoped package context options are retired. A component assessment does not require a package assessment.");
        }
    }

    internal static AssessmentBindings Load(
        string root, string kind, InputManifest input,
        string? packageRevision, byte[]? packageFeedback,
        string? contextRevision, byte[]? contextFeedback)
    {
        RubricLoader.RequireSupportedKind(kind.ToLowerInvariant());
        ComponentReportScope.RejectRetiredInputs(input);
        if (contextRevision is not null || contextFeedback is not null)
        {
            throw new DeterministicValidationException(
                "Scoped package context options are retired. A component assessment does not require a package assessment.");
        }
        if (string.Equals(kind, "component", StringComparison.OrdinalIgnoreCase))
        {
            if (packageRevision is null)
            {
                if (packageFeedback is not null)
                {
                    throw new DeterministicValidationException("Package feedback requires an explicit package revision.");
                }

                return new AssessmentBindings(null, null);
            }

            return new AssessmentBindings(
                RevisionService.LoadPackageBinding(root, Resolve(root, packageRevision), packageFeedback), null);
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

    internal static IReadOnlyList<ImmutableInputSnapshot> ReadFeedbackHistory(string root, CommandOptions options)
    {
        var paths = options.Many("--feedback-history");
        if (paths.Count > ResourceLimits.SupplementalInputCount)
        {
            throw new DeterministicValidationException("Too many historical feedback inputs.");
        }

        var snapshots = new List<ImmutableInputSnapshot>();
        long total = 0;
        foreach (var path in paths)
        {
            var relative = Path.IsPathRooted(path) ? Path.GetRelativePath(root, path) : path;
            var snapshot = ImmutableInputSnapshot.Capture(
                SafePath.ResolveUnderRoot(root, relative, true, true), "historical assessment feedback");
            total += snapshot.Bytes.LongLength;
            BoundedIO.EnsureLength(total, ResourceLimits.SupplementalInputAggregateBytes, "historical feedback");
            snapshots.Add(snapshot);
        }

        return snapshots;
    }

    private static string Resolve(string root, string path) =>
        SafePath.ResolveExistingDirectoryUnderRoot(root,
            Path.IsPathRooted(path) ? path : Path.Combine(root, path));
}
