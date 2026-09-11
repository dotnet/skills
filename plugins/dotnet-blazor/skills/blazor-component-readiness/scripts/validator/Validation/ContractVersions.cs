using System.Text.Json;
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

            var pluginPath = Path.GetFullPath(Path.Combine(skillRoot, "..", "..", "plugin.json"));
            var bytes = BoundedIO.ReadAllBytes(
                pluginPath,
                ResourceLimits.SerializedArtifactBytes,
                "installed plugin manifest");
            using var document = StrictJson.Parse(
                bytes,
                ResourceLimits.SerializedArtifactBytes,
                "installed plugin manifest");
            var root = document.RootElement;
            if (!root.TryGetProperty("version", out var value) || value.ValueKind != JsonValueKind.String)
            {
                throw new DeterministicValidationException(
                    "Installed plugin manifest requires a string version.");
            }

            return ContractJson.NormalizeText(value.GetString(), "plugin version", 64);
        }
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
