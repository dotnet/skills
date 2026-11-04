using System.Text;
using BlazorComponentReadiness.Validator.Contracts;
using BlazorComponentReadiness.Validator.Evidence;
using BlazorComponentReadiness.Validator.IO;
using BlazorComponentReadiness.Validator.Validation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace BlazorComponentReadiness.SourceReader;

public static class SourceMemberReader
{
    public const int SourceByteLimit = 8 * 1024 * 1024;
    public const int PacketByteLimit = 12 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static byte[] Read(string root, string receiptPath, string expectedReceiptSha256,
        string sourcePath, int selectedLine, string outputPath)
    {
        var fullRoot = Path.GetFullPath(root);
        var filesystemRoot = Path.GetPathRoot(fullRoot)
            ?? throw new DeterministicValidationException("The approved root must be an existing directory.");
        _ = SafePath.ResolveExistingDirectoryUnderRoot(filesystemRoot, fullRoot);
        var receiptRelative = Canonicalization.RelativePath(receiptPath, "capture receipt");
        var outputRelative = Canonicalization.RelativePath(outputPath, "member packet");
        var selectedSource = Canonicalization.RelativePath(sourcePath, "captured source path");
        if (!selectedSource.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            throw new DeterministicValidationException("Only captured C# .cs files are supported.");
        EvidenceIdentity.ValidateDigest(new Sha256Digest("sha256", expectedReceiptSha256), "expected capture digest");
        var fullReceipt = SafePath.ResolveUnderRoot(fullRoot, receiptRelative, true, true);
        var receipt = ImmutableInputSnapshot.Capture(fullReceipt, "source capture receipt");
        var receiptDigest = ContractJson.RawDigest(receipt.Bytes);
        if (receiptDigest.Value != expectedReceiptSha256)
            throw new DeterministicValidationException("The capture receipt bytes differ from the expected digest.");
        using var document = StrictJson.Parse(receipt.Bytes, ResourceLimits.SerializedArtifactBytes, "source capture receipt");
        var capture = document.RootElement;
        if (ContractJson.Int32(capture, "schema_version") != 1 ||
            ContractJson.String(capture, "capture_kind") != "source-archive")
            throw new DeterministicValidationException("An existing schema-1 source-archive capture receipt is required.");
        var sourceRoot = Canonicalization.RelativePath(ContractJson.String(capture, "source_root"), "captured source root");
        _ = SafePath.ResolveExistingDirectoryUnderRoot(fullRoot, Path.Combine(fullRoot, sourceRoot));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (outputRelative.Equals(sourceRoot, comparison) || outputRelative.StartsWith(sourceRoot + "/", comparison))
            throw new DeterministicValidationException("The member packet must be outside the captured source tree.");
        var matches = ContractJson.Array(capture, "source_artifacts").EnumerateArray()
            .Where(item => ContractJson.String(item, "source_path") == selectedSource).ToArray();
        if (matches.Length != 1)
            throw new DeterministicValidationException("The source path must select exactly one captured artifact.");
        var artifact = matches[0];
        var contentPath = Canonicalization.RelativePath(ContractJson.String(artifact, "content_path"), "captured content path");
        if (contentPath != sourceRoot + "/" + selectedSource)
            throw new DeterministicValidationException("Captured content_path does not correspond to source_root/source_path.");
        var capturedDigest = ContractJson.String(artifact, "content_sha256");
        EvidenceIdentity.ValidateDigest(new Sha256Digest("sha256", capturedDigest), "captured source digest");
        if (ContractJson.String(artifact, "archive_content_sha256") != capturedDigest)
            throw new DeterministicValidationException("Captured archive and local source digests disagree.");
        var fullSource = SafePath.ResolveUnderRoot(fullRoot, contentPath, true, true);
        var source = ImmutableInputSnapshot.Capture(fullSource, SourceByteLimit, "captured C# source");
        var sourceDigest = ContractJson.RawDigest(source.Bytes);
        if (sourceDigest.Value != capturedDigest)
            throw new DeterministicValidationException("The captured source bytes changed.");
        var destination = SafePath.ResolveUnderRoot(fullRoot, outputRelative, false);
        if (File.Exists(destination) || Directory.Exists(destination))
            throw new DeterministicValidationException("The member packet output already exists; choose a new filename.");

        var bomLength = source.Bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) ? 3 : 0;
        string original;
        try { original = Utf8.GetString(source.Bytes.AsSpan(bomLength)); }
        catch (DecoderFallbackException error)
        {
            throw new DeterministicValidationException("Captured C# source must be strict UTF-8.", error);
        }
        var text = SourceText.From(original, Utf8);
        var tree = CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.Preview));
        var syntaxError = tree.GetDiagnostics().FirstOrDefault(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        if (syntaxError is not null)
            throw new DeterministicValidationException($"Invalid C# syntax ({syntaxError.Id}); no member packet was produced.");
        if (selectedLine < 1 || selectedLine > text.Lines.Count)
            throw new DeterministicValidationException("The selected physical line is outside the captured file.");
        var syntax = tree.GetRoot();
        if (syntax.DescendantTrivia(descendIntoTrivia: true).Any(trivia =>
                trivia.IsKind(SyntaxKind.DisabledTextTrivia) && ContainsLine(text, trivia.Span, selectedLine)))
            throw new DeterministicValidationException("An inactive preprocessor selection cannot establish a complete member.");
        var members = syntax.DescendantNodes()
            .Where(node => node is MethodDeclarationSyntax or ConstructorDeclarationSyntax)
            .Where(node => ContainsLine(text, node.Span, selectedLine)).ToArray();
        if (members.Length != 1)
            throw new DeterministicValidationException("The selected line must be inside exactly one supported method or constructor.");
        var member = (BaseMethodDeclarationSyntax)members[0];
        if (member.Body is null && member.ExpressionBody is null)
            throw new DeterministicValidationException("A declaration without a body is not a supported complete member.");
        var memberText = text.ToString(member.Span);
        var memberBytes = Utf8.GetBytes(memberText);
        var startByte = bomLength + Utf8.GetByteCount(original.AsSpan(0, member.Span.Start));
        var endByte = startByte + memberBytes.Length;
        if (!source.Bytes.AsSpan(startByte, memberBytes.Length).SequenceEqual(memberBytes))
            throw new DeterministicValidationException("The member text does not match the original UTF-8 byte slice.");
        var start = text.Lines.GetLinePosition(member.Span.Start);
        var end = text.Lines.GetLinePosition(member.Span.End);
        var endLine = text.Lines.GetLineFromPosition(member.Span.End - 1).LineNumber + 1;
        var packet = StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", 1);
            writer.WriteString("packet_kind", "csharp-complete-member");
            writer.WriteString("receipt_path", receiptRelative);
            ContractJson.WriteDigest(writer, "receipt_sha256", receiptDigest);
            writer.WriteString("source_path", selectedSource);
            writer.WriteString("content_path", contentPath);
            ContractJson.WriteDigest(writer, "source_sha256", sourceDigest);
            writer.WriteNumber("selected_line", selectedLine);
            writer.WriteString("member_kind", member is ConstructorDeclarationSyntax ? "constructor" : "method");
            writer.WriteNumber("start_line", start.Line + 1);
            writer.WriteNumber("end_line", endLine);
            writer.WriteNumber("start_column", start.Character + 1);
            writer.WriteNumber("end_column_exclusive", end.Character + 1);
            writer.WriteNumber("start_byte", startByte);
            writer.WriteNumber("end_byte_exclusive", endByte);
            ContractJson.WriteDigest(writer, "member_text_sha256", ContractJson.RawDigest(memberBytes));
            writer.WriteString("original_text", memberText);
            writer.WriteEndObject();
        });
        byte[] persisted = [.. packet, (byte)'\n'];
        BoundedIO.EnsureLength(persisted.Length, PacketByteLimit, "complete member packet");
        _ = SafePath.ResolveUnderRoot(fullRoot, contentPath, true, true);
        _ = SafePath.ResolveUnderRoot(fullRoot, receiptRelative, true, true);
        source.EnsureUnchanged();
        receipt.EnsureUnchanged();
        AtomicFile.WriteNew(fullRoot, outputRelative, persisted);
        return persisted;
    }

    private static bool ContainsLine(SourceText text, TextSpan span, int line) =>
        span.Length > 0 && text.Lines.GetLineFromPosition(span.Start).LineNumber + 1 <= line &&
        text.Lines.GetLineFromPosition(span.End - 1).LineNumber + 1 >= line;
}
