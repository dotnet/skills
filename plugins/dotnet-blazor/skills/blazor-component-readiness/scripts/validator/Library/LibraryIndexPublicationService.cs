using System.Text.Json;
using BlazorComponentReadiness.Validator.Contracts;
using BlazorComponentReadiness.Validator.IO;
using BlazorComponentReadiness.Validator.Validation;

namespace BlazorComponentReadiness.Validator.Library;

public sealed record LibraryIndexPointer(
    int SchemaVersion,
    string GenerationId,
    string JsonPath,
    Sha256Digest JsonDigest,
    string MarkdownPath,
    Sha256Digest MarkdownDigest);

public static class LibraryIndexPublicationService
{
    public const int SchemaVersion = 1;
    internal static Action? AfterGenerationCommitForTests { get; set; }
    internal static Action? BeforePointerUpdateForTests { get; set; }
    internal static Action? AfterPointerUpdateForTests { get; set; }

    public static LibraryIndexPointer Publish(
        string root,
        string jsonRelativePath,
        string markdownRelativePath,
        LibraryIndexArtifacts index)
    {
        var jsonPath = SafePath.ResolveUnderRoot(root, jsonRelativePath, requireExisting: false);
        var markdownPath = SafePath.ResolveUnderRoot(root, markdownRelativePath, requireExisting: false);
        var outputParent = Path.GetDirectoryName(jsonPath)!;
        if (!string.Equals(
                outputParent,
                Path.GetDirectoryName(markdownPath),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new DeterministicValidationException(
                "Library index JSON and Markdown compatibility paths must share one output parent.");
        }

        var publicationRoot = SafePath.ResolveUnderRoot(
            outputParent,
            ".readiness-index",
            requireExisting: false);
        Directory.CreateDirectory(publicationRoot);
        var generationsRoot = SafePath.ResolveUnderRoot(
            outputParent,
            ".readiness-index/generations",
            requireExisting: false);
        Directory.CreateDirectory(generationsRoot);
        RecoverStaging(generationsRoot);

        var generationDirectory = Path.Combine(generationsRoot, index.GenerationId);
        if (!Directory.Exists(generationDirectory))
        {
            AtomicDirectory.WriteNew(generationsRoot, index.GenerationId, staging =>
            {
                AtomicFile.WriteNew(staging, "library-index.json", index.Json);
                AtomicFile.WriteNew(staging, "library-index.md", index.Markdown);
            });
        }

        var pointer = new LibraryIndexPointer(
            SchemaVersion,
            index.GenerationId,
            $".readiness-index/generations/{index.GenerationId}/library-index.json",
            ContractJson.RawDigest(index.Json),
            $".readiness-index/generations/{index.GenerationId}/library-index.md",
            ContractJson.RawDigest(index.Markdown));
        ValidateGeneration(outputParent, pointer);
        AfterGenerationCommitForTests?.Invoke();

        var pointerRelative = ".readiness-index/current-generation.json";
        BeforePointerUpdateForTests?.Invoke();
        AtomicFile.Replace(root, Relative(root, Path.Combine(outputParent, pointerRelative)), Serialize(pointer));
        AfterPointerUpdateForTests?.Invoke();

        RepairCompatibilityCache(root, jsonRelativePath, index.Json);
        RepairCompatibilityCache(root, markdownRelativePath, index.Markdown);
        ValidateGeneration(outputParent, Parse(BoundedIO.ReadAllBytes(
            Path.Combine(publicationRoot, "current-generation.json"),
            ResourceLimits.SerializedArtifactBytes,
            "library index current-generation pointer")));
        ValidateCompatibilityCache(root, jsonRelativePath, index.Json);
        ValidateCompatibilityCache(root, markdownRelativePath, index.Markdown);
        return pointer;
    }

    public static void RecoverCurrent(
        string root,
        string jsonRelativePath = "library-index.json",
        string markdownRelativePath = "library-index.md")
    {
        var pointerPath = SafePath.ResolveUnderRoot(
            root,
            ".readiness-index/current-generation.json",
            requireExisting: false);
        if (!File.Exists(pointerPath))
        {
            return;
        }

        SafePath.EnsureRegularFile(pointerPath);
        var pointer = Parse(BoundedIO.ReadAllBytes(
            pointerPath,
            ResourceLimits.SerializedArtifactBytes,
            "library index current-generation pointer"));
        ValidateGeneration(root, pointer);
        RepairCompatibilityCache(
            root,
            jsonRelativePath,
            ReadGenerationFile(root, pointer.JsonPath, "library index generation JSON"));
        RepairCompatibilityCache(
            root,
            markdownRelativePath,
            ReadGenerationFile(root, pointer.MarkdownPath, "library index generation Markdown"));
        ValidateCompatibilityCache(
            root,
            jsonRelativePath,
            ReadGenerationFile(root, pointer.JsonPath, "library index generation JSON"));
        ValidateCompatibilityCache(
            root,
            markdownRelativePath,
            ReadGenerationFile(root, pointer.MarkdownPath, "library index generation Markdown"));
    }

    public static byte[] Serialize(LibraryIndexPointer pointer) =>
        StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", pointer.SchemaVersion);
            writer.WriteString("generation_id", pointer.GenerationId);
            writer.WriteString("json_path", pointer.JsonPath);
            ContractJson.WriteDigest(writer, "json_sha256", pointer.JsonDigest);
            writer.WriteString("markdown_path", pointer.MarkdownPath);
            ContractJson.WriteDigest(writer, "markdown_sha256", pointer.MarkdownDigest);
            writer.WriteEndObject();
        });

    public static LibraryIndexPointer Parse(ReadOnlyMemory<byte> bytes)
    {
        using var document = StrictJson.Parse(
            bytes,
            ResourceLimits.SerializedArtifactBytes,
            "library index current-generation pointer");
        var root = document.RootElement;
        ContractJson.RequireProperties(
            root,
            "schema_version",
            "generation_id",
            "json_path",
            "json_sha256",
            "markdown_path",
            "markdown_sha256");
        var pointer = new LibraryIndexPointer(
            ContractJson.Int32(root, "schema_version"),
            ContractJson.String(root, "generation_id"),
            ContractJson.String(root, "json_path"),
            ContractJson.Digest(ContractJson.Object(root, "json_sha256")),
            ContractJson.String(root, "markdown_path"),
            ContractJson.Digest(ContractJson.Object(root, "markdown_sha256")));
        if (pointer.SchemaVersion != SchemaVersion ||
            !pointer.GenerationId.StartsWith("GEN1-", StringComparison.Ordinal) ||
            pointer.GenerationId.Length != 69 ||
            pointer.GenerationId["GEN1-".Length..].Any(character =>
                character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')) ||
            pointer.JsonPath !=
                $".readiness-index/generations/{pointer.GenerationId}/library-index.json" ||
            pointer.MarkdownPath !=
                $".readiness-index/generations/{pointer.GenerationId}/library-index.md")
        {
            throw new DeterministicValidationException(
                "Library index current-generation pointer has invalid schema or generation ID.");
        }

        _ = Canonicalization.RelativePath(pointer.JsonPath, "library index JSON generation path");
        _ = Canonicalization.RelativePath(pointer.MarkdownPath, "library index Markdown generation path");
        ContractJson.RequireCanonical(bytes.Span, Serialize(pointer), "library index current-generation pointer");
        return pointer;
    }

    private static void ValidateGeneration(string outputParent, LibraryIndexPointer pointer)
    {
        var generationDirectory = SafePath.ResolveExistingDirectoryUnderRoot(
            outputParent,
            Path.Combine(outputParent, ".readiness-index", "generations", pointer.GenerationId));
        var expectedFiles = new[] { "library-index.json", "library-index.md" };
        if (Directory.GetDirectories(generationDirectory, "*", SearchOption.TopDirectoryOnly).Length != 0 ||
            !Directory.GetFiles(generationDirectory, "*", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Order(StringComparer.Ordinal)
                .SequenceEqual(expectedFiles.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw new DeterministicValidationException(
                "Library index generation must contain exactly one JSON and one Markdown regular file.");
        }

        var json = ReadGenerationFile(outputParent, pointer.JsonPath, "library index generation JSON");
        var markdown = ReadGenerationFile(
            outputParent,
            pointer.MarkdownPath,
            "library index generation Markdown");
        if (ContractJson.RawDigest(json) != pointer.JsonDigest ||
            ContractJson.RawDigest(markdown) != pointer.MarkdownDigest)
        {
            throw new DeterministicValidationException(
                "Library index current-generation pointer names a mismatched or corrupt generation.");
        }

        using var jsonDocument = StrictJson.Parse(
            json,
            ResourceLimits.SerializedArtifactBytes,
            "library index generation JSON");
        var jsonGeneration = ContractJson.String(jsonDocument.RootElement, "generation_id");
        var markdownText = System.Text.Encoding.UTF8.GetString(markdown);
        if (jsonGeneration != pointer.GenerationId ||
            !markdownText.Contains($"**Generation:** `{pointer.GenerationId}`", StringComparison.Ordinal))
        {
            throw new DeterministicValidationException(
                "Library index JSON and Markdown do not name the pointer generation.");
        }
    }

    private static byte[] ReadGenerationFile(string outputParent, string relativePath, string resource)
    {
        var path = SafePath.ResolveUnderRoot(
            outputParent,
            relativePath,
            requireExisting: true,
            requireFile: true);
        SafePath.EnsureRegularFile(path);
        return BoundedIO.ReadAllBytes(path, ResourceLimits.SerializedArtifactBytes, resource);
    }

    private static void RepairCompatibilityCache(string root, string relativePath, byte[] expected)
    {
        var path = SafePath.ResolveUnderRoot(root, relativePath, requireExisting: false);
        if (File.Exists(path))
        {
            SafePath.EnsureRegularFile(path);
            var actual = BoundedIO.ReadAllBytes(
                path,
                ResourceLimits.SerializedArtifactBytes,
                "library index compatibility cache");
            if (actual.AsSpan().SequenceEqual(expected))
            {
                return;
            }
        }

        AtomicFile.Replace(root, relativePath, expected);
    }

    private static void ValidateCompatibilityCache(
        string root,
        string relativePath,
        byte[] expected)
    {
        var path = SafePath.ResolveUnderRoot(
            root,
            relativePath,
            requireExisting: true,
            requireFile: true);
        var actual = BoundedIO.ReadAllBytes(
            path,
            ResourceLimits.SerializedArtifactBytes,
            "library index compatibility cache");
        if (!actual.AsSpan().SequenceEqual(expected))
        {
            throw new DeterministicValidationException(
                "Library index compatibility cache differs from the authoritative generation.");
        }
    }

    private static void RecoverStaging(string generationsRoot)
    {
        foreach (var directory in Directory.GetDirectories(
                     generationsRoot,
                     ".*.staging",
                     SearchOption.TopDirectoryOnly))
        {
            var safe = SafePath.ResolveExistingDirectoryUnderRoot(generationsRoot, directory);
            Directory.Delete(safe, recursive: true);
        }
    }

    private static string Relative(string root, string path) =>
        Canonicalization.RelativePath(
            Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path)).Replace('\\', '/'),
            "library index publication path");
}
