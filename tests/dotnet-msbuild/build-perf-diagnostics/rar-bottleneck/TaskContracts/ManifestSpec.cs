namespace BuildTaskContracts;

/// <summary>Defines the manifest format consumed by the build-only task.</summary>
public static class ManifestSpec
{
    /// <summary>Gets the first line written to each generated manifest.</summary>
    public const string Header = "gateway-manifest-v1";
}
