using System.Text.Json;
using System.Text.RegularExpressions;
using BlazorComponentReadiness.Validator.IO;

namespace BlazorComponentReadiness.Validator.Validation;

public static class ContractVersions
{
    public static string PluginVersion
    {
        get
        {
            var skillRoot = Environment.GetEnvironmentVariable("READINESS_SKILL_ROOT");
            if (string.IsNullOrWhiteSpace(skillRoot))
            {
                throw new DeterministicValidationException(
                    "READINESS_SKILL_ROOT is required to resolve the installed plugin version.");
            }

            skillRoot = ResolvePhysicalDirectory(skillRoot);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!string.Equals(Path.GetFileName(skillRoot), "blazor-component-readiness", comparison) ||
                !File.Exists(Path.Combine(skillRoot, "SKILL.md")))
            {
                throw new DeterministicValidationException(
                    "READINESS_SKILL_ROOT must identify the installed or isolated blazor-component-readiness skill.");
            }

            var snapshot = ReadManifest(Path.Combine(skillRoot, "plugin-manifest.json"), "bundled plugin manifest");
            if (string.Equals(Path.GetFileName(Path.GetDirectoryName(skillRoot)), "skills", comparison))
            {
                var installed = ReadManifest(
                    Path.Combine(skillRoot, "..", "..", "plugin.json"), "installed plugin manifest");
                if (!installed.Bytes.AsSpan().SequenceEqual(snapshot.Bytes))
                {
                    throw new DeterministicValidationException(
                        "Bundled plugin manifest does not match the authoritative installed plugin manifest.");
                }

                return installed.Version;
            }

            return snapshot.Version;
        }
    }

    private static (byte[] Bytes, string Version) ReadManifest(string path, string subject)
    {
        var bytes = BoundedIO.ReadAllBytes(path, ResourceLimits.SerializedArtifactBytes, subject);
        using var document = StrictJson.Parse(bytes, ResourceLimits.SerializedArtifactBytes, subject);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String ||
            name.GetString() != "dotnet-blazor")
        {
            throw new DeterministicValidationException($"{subject} requires the dotnet-blazor identity.");
        }

        if (!root.TryGetProperty("version", out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new DeterministicValidationException($"{subject} requires a string version.");
        }

        var version = value.GetString()!;
        if (!Regex.IsMatch(version, @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z") ||
            NuGetVersionNormalizer.Normalize(version) != version)
        {
            throw new DeterministicValidationException($"{subject} requires a canonical major.minor.patch version.");
        }

        return (bytes, version);
    }

    private static string ResolvePhysicalDirectory(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full)
            ?? throw new DeterministicValidationException("Skill directory has no filesystem root.");
        var current = root;
        foreach (var segment in Path.GetRelativePath(root, full).Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var directory = new DirectoryInfo(Path.Combine(current, segment));
            if (!directory.Exists)
                throw new DirectoryNotFoundException($"Skill directory does not exist: {directory.FullName}");
            current = (directory.Attributes & FileAttributes.ReparsePoint) != 0
                ? ResolvePhysicalDirectory((directory.ResolveLinkTarget(true)
                    ?? throw new IOException($"Could not resolve skill directory link: {directory.FullName}")).FullName)
                : directory.FullName;
        }

        return Path.GetFullPath(current);
    }

    public static string ValidatorVersion
    {
        get
        {
            var version = typeof(ContractVersions).Assembly.GetName().Version
                ?? throw new DeterministicValidationException(
                    "Validator assembly does not declare a version.");
            return $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }
}

public static class RendererContract
{
    public const string Version = "1.0.0";
}
