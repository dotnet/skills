namespace BlazorComponentReadiness.Validator.Contracts;

public sealed record PackageIdentity(
    string Id,
    string Version,
    string NupkgSha256,
    long NupkgSize,
    string NuspecEntry);
