namespace BlazorComponentReadiness.Validator.IO;

public static class ResourceLimits
{
    public const long SerializedArtifactBytes = 64L * 1024 * 1024;
    public const long AuthoredLedgerBytes = 4L * 1024 * 1024;
    public const long NupkgBytes = 256L * 1024 * 1024;
    public const long NuspecBytes = 1L * 1024 * 1024;
    public const int SupplementalInputCount = 32;
    public const long SupplementalInputAggregateBytes = 64L * 1024 * 1024;
    public const int RetrievalAttemptCount = 64;
    public const long SourceArchiveBytes = 256L * 1024 * 1024;
    public const long SourceArchiveExpandedBytes = 256L * 1024 * 1024;
    public const int SourceArchiveEntryCount = 100_000;
    public const int SourceArchiveSelectionCount = 1_024;
}
