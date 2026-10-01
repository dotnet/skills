using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace UnskipClosedTests.Tool;

internal static class ApplyEngine
{
    private sealed record SourceEdit(string Path, int Start, int Length, Candidate Candidate);
    private sealed record VerificationOutcome(bool Success, string Reason);

    public static async Task<ApplyResult> ApplyAsync(
        string requestedRoot,
        ToolConfig config,
        string manifestPath,
        string agentOutputPath,
        string? evidencePath)
    {
        Manifest requestedManifest = ManifestValidator.Read(manifestPath);
        if (!string.Equals(requestedManifest.ConfigDigest, ConfigLoader.Digest(config), StringComparison.Ordinal))
        {
            throw new ContractException("Apply config does not match manifest config_digest.");
        }

        List<string> selectedIds = ReadAgentSelection(agentOutputPath, requestedManifest.ManifestDigest);
        Dictionary<string, Candidate> requestedCandidates = requestedManifest.Candidates
            .ToDictionary(static candidate => candidate.CandidateId, StringComparer.Ordinal);
        foreach (string candidateId in selectedIds)
        {
            if (!requestedCandidates.TryGetValue(candidateId, out Candidate? candidate))
            {
                throw new ContractException($"Agent output selected unknown candidate_id '{candidateId}'.");
            }

            if (!candidate.Decision.Eligible)
            {
                throw new ContractException($"Agent output selected ineligible candidate_id '{candidateId}'.");
            }
        }

        GitRepository repository = GitRepository.Open(requestedRoot, requestedManifest.Repository);
        if (!string.Equals(repository.Commit, requestedManifest.SourceCommit, StringComparison.Ordinal))
        {
            throw new ContractException(
                $"Manifest source_commit {requestedManifest.SourceCommit} is stale; checked out commit is {repository.Commit}.");
        }

        Manifest freshInventory = InventoryEngine.Create(repository.Root, requestedManifest.Repository, config);
        Manifest freshResolved = await IssueResolver.ResolveAsync(freshInventory, evidencePath);
        if (!string.Equals(freshResolved.ManifestDigest, requestedManifest.ManifestDigest, StringComparison.Ordinal))
        {
            throw new ContractException(
                "Re-inventory/re-resolution did not reproduce the trusted manifest; source, owners, spans, references, or evidence are stale.");
        }

        if (selectedIds.Count == 0)
        {
            return EmptyResult(requestedManifest.SourceCommit, requestedManifest.ManifestDigest);
        }

        Dictionary<string, Candidate> candidates = freshResolved.Candidates
            .ToDictionary(static candidate => candidate.CandidateId, StringComparer.Ordinal);
        List<SourceEdit> edits = selectedIds
            .Select(candidateId => CreateEdit(repository, config, candidates[candidateId]))
            .OrderBy(static edit => edit.Path, StringComparer.Ordinal)
            .ThenByDescending(static edit => edit.Start)
            .ToList();
        RejectOverlappingEdits(edits);

        Dictionary<string, byte[]> originalBytes = edits
            .Select(static edit => edit.Path)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(
                static path => path,
                path => ReadBytes(PathRules.ResolveInsideRoot(repository.Root, path, "candidate path")),
                StringComparer.Ordinal);
        Dictionary<string, byte[]> expectedBytes = originalBytes.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.ToArray(),
            StringComparer.Ordinal);
        List<string> retained = [];
        List<RevertedCandidateResult> reverted = [];

        string workRoot = Path.Combine(
            repository.MetadataDirectory(),
            "unskip-closed-tests",
            requestedManifest.ManifestDigest);
        try
        {
            ResetDirectory(workRoot);
            foreach (SourceEdit edit in edits)
            {
                string fullPath = PathRules.ResolveInsideRoot(repository.Root, edit.Path, "candidate path");
                byte[] beforeCandidate = expectedBytes[edit.Path].ToArray();
                byte[] editedBytes = ApplyTextEdit(beforeCandidate, edit.Start, edit.Length);
                WriteBytes(fullPath, editedBytes);
                expectedBytes[edit.Path] = editedBytes;

                VerificationOutcome outcome = await VerifyCandidateAsync(
                    repository,
                    config,
                    requestedManifest,
                    edit.Candidate,
                    workRoot);
                string? mutation = RestoreUnexpectedSourceMutations(repository, expectedBytes);
                if (mutation is not null)
                {
                    outcome = new VerificationOutcome(false, $"verification_mutated_source:{mutation}");
                }

                if (outcome.Success)
                {
                    retained.Add(edit.Candidate.CandidateId);
                }
                else
                {
                    expectedBytes[edit.Path] = beforeCandidate;
                    WriteBytes(fullPath, beforeCandidate);
                    reverted.Add(CreateRevertedCandidate(edit.Candidate, outcome.Reason));
                }
            }
        }
        catch
        {
            RestoreFiles(repository, originalBytes);
            throw;
        }
        finally
        {
            TryDeleteDirectory(workRoot);
        }

        if (retained.Count == 0)
        {
            RestoreFiles(repository, originalBytes);
            return new ApplyResult
            {
                SourceCommit = requestedManifest.SourceCommit,
                ManifestDigest = requestedManifest.ManifestDigest,
                RevertedCandidates = reverted.OrderBy(static item => item.CandidateId, StringComparer.Ordinal).ToList(),
                HasChanges = false,
                PrTitle = "",
                PrBody = "",
            };
        }

        Manifest finalEligibility;
        try
        {
            finalEligibility = await IssueResolver.ResolveAsync(freshInventory, evidencePath);
        }
        catch
        {
            RestoreFiles(repository, originalBytes);
            throw;
        }

        HashSet<string> finallyEligible = finalEligibility.Candidates
            .Where(static candidate => candidate.Decision.Eligible)
            .Select(static candidate => candidate.CandidateId)
            .ToHashSet(StringComparer.Ordinal);
        List<string> expiredCandidates = retained
            .Where(candidateId => !finallyEligible.Contains(candidateId))
            .ToList();
        if (expiredCandidates.Count > 0)
        {
            reverted.AddRange(expiredCandidates.Select(candidateId =>
                CreateRevertedCandidate(candidates[candidateId], "eligibility_changed_after_verification")));
            retained = retained.Except(expiredCandidates, StringComparer.Ordinal).ToList();
            RestoreFiles(repository, originalBytes);
            if (retained.Count > 0)
            {
                HashSet<string> retainedSet = retained.ToHashSet(StringComparer.Ordinal);
                Dictionary<string, byte[]> rebuiltBytes = originalBytes.ToDictionary(
                    static pair => pair.Key,
                    static pair => pair.Value.ToArray(),
                    StringComparer.Ordinal);
                foreach (SourceEdit edit in edits.Where(edit => retainedSet.Contains(edit.Candidate.CandidateId)))
                {
                    rebuiltBytes[edit.Path] = ApplyTextEdit(
                        rebuiltBytes[edit.Path],
                        edit.Start,
                        edit.Length);
                }

                RestoreFiles(repository, rebuiltBytes);
            }
        }

        if (retained.Count == 0)
        {
            return new ApplyResult
            {
                SourceCommit = requestedManifest.SourceCommit,
                ManifestDigest = requestedManifest.ManifestDigest,
                RevertedCandidates = reverted
                    .OrderBy(static item => item.CandidateId, StringComparer.Ordinal)
                    .ToList(),
                HasChanges = false,
            };
        }

        Dictionary<string, Candidate> finalCandidates = finalEligibility.Candidates
            .ToDictionary(static candidate => candidate.CandidateId, StringComparer.Ordinal);
        List<Candidate> retainedCandidates = retained.Select(id => finalCandidates[id])
            .OrderBy(static candidate => candidate.Path, StringComparer.Ordinal)
            .ThenBy(static candidate => candidate.AttributeSpan.Start)
            .ToList();
        return new ApplyResult
        {
            SourceCommit = requestedManifest.SourceCommit,
            ManifestDigest = requestedManifest.ManifestDigest,
            RetainedCandidates = retainedCandidates
                .Select(static candidate => new RetainedCandidateResult
                {
                    CandidateId = candidate.CandidateId,
                    Path = candidate.Path,
                    TestFqns = candidate.Owner.TestFqns.Order(StringComparer.Ordinal).ToList(),
                })
                .OrderBy(static candidate => candidate.CandidateId, StringComparer.Ordinal)
                .ToList(),
            RevertedCandidates = reverted.OrderBy(static item => item.CandidateId, StringComparer.Ordinal).ToList(),
            ChangedPaths = retainedCandidates
                .Select(static candidate => candidate.Path)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList(),
            HasChanges = true,
            PrTitle = retained.Count == 1
                ? "Unskip test for completed GitHub work item"
                : $"Unskip {retained.Count} tests for completed GitHub work items",
            PrBody = CreatePrBody(requestedManifest, retainedCandidates, reverted),
        };
    }

    private static List<string> ReadAgentSelection(string path, string expectedManifestDigest)
    {
        using JsonDocument document = JsonSupport.ReadDocument(path);
        JsonElement root = document.RootElement;
        List<JsonElement> items = [];
        if (root.ValueKind == JsonValueKind.Array)
        {
            items.AddRange(root.EnumerateArray());
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            JsonElement collection;
            if (root.TryGetProperty("items", out collection) ||
                root.TryGetProperty("outputs", out collection) ||
                root.TryGetProperty("safe_outputs", out collection))
            {
                foreach (JsonProperty property in root.EnumerateObject())
                {
                    if (property.Name is not ("items" or "outputs" or "safe_outputs"))
                    {
                        throw new ContractException($"Unknown agent output root field '{property.Name}'.");
                    }
                }

                if (collection.ValueKind != JsonValueKind.Array)
                {
                    throw new ContractException("Agent output item collection must be an array.");
                }

                items.AddRange(collection.EnumerateArray());
            }
            else if (root.TryGetProperty("type", out _))
            {
                items.Add(root);
            }
            else
            {
                throw new ContractException("Agent output must contain an item array.");
            }
        }
        else
        {
            throw new ContractException("Agent output root must be an object or array.");
        }

        if (items.Count != 1 || items[0].ValueKind != JsonValueKind.Object)
        {
            throw new ContractException("Agent output must contain exactly one output item.");
        }

        JsonElement item = items[0];
        HashSet<string> allowed = ["type", "manifest_digest", "candidate_ids_json"];
        foreach (JsonProperty property in item.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
            {
                throw new ContractException($"Unknown apply_verified_unskips field '{property.Name}'.");
            }
        }

        string type = RequiredString(item, "type");
        if (type != "apply_verified_unskips")
        {
            throw new ContractException($"Unexpected output item type '{type}'.");
        }

        string manifestDigest = RequiredString(item, "manifest_digest");
        if (!string.Equals(manifestDigest, expectedManifestDigest, StringComparison.Ordinal))
        {
            throw new ContractException("Agent output manifest_digest does not match the resolved manifest.");
        }

        if (!item.TryGetProperty("candidate_ids_json", out JsonElement idsElement))
        {
            throw new ContractException("candidate_ids_json is required.");
        }

        JsonElement array;
        JsonDocument? parsedString = null;
        if (idsElement.ValueKind == JsonValueKind.String)
        {
            try
            {
                parsedString = JsonDocument.Parse(idsElement.GetString()!);
                array = parsedString.RootElement;
            }
            catch (JsonException ex)
            {
                throw new ContractException($"candidate_ids_json string is malformed JSON: {ex.Message}");
            }
        }
        else
        {
            array = idsElement;
        }

        try
        {
            if (array.ValueKind != JsonValueKind.Array)
            {
                throw new ContractException("candidate_ids_json must be a JSON array.");
            }

            List<string> ids = [];
            foreach (JsonElement id in array.EnumerateArray())
            {
                if (id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(id.GetString()))
                {
                    throw new ContractException("candidate_ids_json must contain only non-empty strings.");
                }

                ids.Add(id.GetString()!);
            }

            if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
            {
                throw new ContractException("candidate_ids_json contains duplicate candidate IDs.");
            }

            return ids;
        }
        finally
        {
            parsedString?.Dispose();
        }
    }

    private static SourceEdit CreateEdit(GitRepository repository, ToolConfig config, Candidate candidate)
    {
        if (PathRules.MatchesAnyGlob(candidate.Path, config.ExcludedGlobs.Concat(config.GeneratedGlobs)))
        {
            throw new ContractException($"Candidate path '{candidate.Path}' is excluded or generated.");
        }

        string fullPath = PathRules.ResolveInsideRoot(repository.Root, candidate.Path, "candidate path");
        PathRules.RejectReparsePoints(repository.Root, fullPath);
        byte[] bytes = ReadBytes(fullPath);
        if (!string.Equals(JsonSupport.Sha256(bytes), candidate.SourceSha256, StringComparison.Ordinal))
        {
            throw new ContractException($"Candidate path '{candidate.Path}' source hash is stale.");
        }

        string text = DecodeUtf8(bytes, out _);
        SyntaxTree tree = CSharpSyntaxTree.ParseText(text, path: candidate.Path);
        CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
        List<AttributeSyntax> matches = root.DescendantNodes().OfType<AttributeSyntax>()
            .Where(attribute => attribute.SpanStart == candidate.AttributeSpan.Start &&
                                attribute.Span.Length == candidate.AttributeSpan.Length)
            .ToList();
        if (matches.Count != 1 ||
            !InventoryEngine.AttributeMatches(matches[0], config.IgnoreAttributeNames) ||
            !string.Equals(
                JsonSupport.Sha256(text.Substring(matches[0].SpanStart, matches[0].Span.Length)),
                candidate.AttributeTextSha256,
                StringComparison.Ordinal))
        {
            throw new ContractException($"Candidate '{candidate.CandidateId}' attribute anchor is stale or fabricated.");
        }

        AttributeSyntax attribute = matches[0];
        AttributeListSyntax list = attribute.Parent as AttributeListSyntax
            ?? throw new ContractException($"Candidate '{candidate.CandidateId}' attribute is not in an attribute list.");
        int index = list.Attributes.IndexOf(attribute);
        TextSpan removal;
        if (list.Attributes.Count == 1)
        {
            removal = list.Span;
        }
        else if (index < list.Attributes.Count - 1)
        {
            removal = TextSpan.FromBounds(attribute.SpanStart, list.Attributes[index + 1].SpanStart);
        }
        else
        {
            removal = TextSpan.FromBounds(list.Attributes[index - 1].Span.End, attribute.Span.End);
        }

        return new SourceEdit(candidate.Path, removal.Start, removal.Length, candidate);
    }

    private static void RejectOverlappingEdits(List<SourceEdit> edits)
    {
        foreach (IGrouping<string, SourceEdit> group in edits.GroupBy(static edit => edit.Path, StringComparer.Ordinal))
        {
            SourceEdit? previous = null;
            foreach (SourceEdit edit in group.OrderBy(static item => item.Start))
            {
                if (previous is not null && edit.Start < previous.Start + previous.Length)
                {
                    throw new ContractException(
                        $"Candidate edits '{previous.Candidate.CandidateId}' and '{edit.Candidate.CandidateId}' overlap.");
                }

                previous = edit;
            }
        }
    }

    private static async Task<VerificationOutcome> VerifyCandidateAsync(
        GitRepository repository,
        ToolConfig config,
        Manifest manifest,
        Candidate candidate,
        string workRoot)
    {
        string candidateDirectory = Path.Combine(workRoot, candidate.CandidateId);
        ResetDirectory(candidateDirectory);
        List<VerificationTest> tests = candidate.Owner.TestFqns
            .Order(StringComparer.Ordinal)
            .Select((fqn, index) => new VerificationTest
            {
                Fqn = fqn,
                SourcePath = candidate.Path,
                ResultFile = Path.GetFullPath(Path.Combine(candidateDirectory, $"{index:D4}.trx")),
            })
            .ToList();
        ApplyRequest request = new()
        {
            Candidate = new VerificationCandidateRequest
            {
                CandidateId = candidate.CandidateId,
            },
            Repository = manifest.Repository,
            SourceCommit = manifest.SourceCommit,
            Tests = tests,
        };
        string requestPath = Path.GetFullPath(Path.Combine(candidateDirectory, "request.json"));
        JsonSupport.Write(requestPath, request);

        List<string> argv = [.. config.VerificationCommand, requestPath];

        ProcessStartInfo startInfo = new(argv[0])
        {
            WorkingDirectory = repository.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in argv.Skip(1))
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using Process process = Process.Start(startInfo)
                ?? throw new InfrastructureException($"Could not start verification command '{argv[0]}'.");
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(config.VerificationTimeoutSeconds));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                await Task.WhenAll(stdout, stderr);
                return new VerificationOutcome(false, "verification_timeout");
            }

            string standardError = await stderr;
            await stdout;
            if (process.ExitCode != 0)
            {
                string detail = standardError.Trim();
                if (detail.Length > 160)
                {
                    detail = detail[..160];
                }

                return new VerificationOutcome(
                    false,
                    detail.Length == 0
                        ? $"verification_nonzero_exit:{process.ExitCode}"
                        : $"verification_nonzero_exit:{process.ExitCode}:{detail}");
            }
        }
        catch (InfrastructureException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw new InfrastructureException($"Could not invoke verification command '{argv[0]}'.", ex);
        }

        (bool success, string reason) = TrxVerifier.Verify(tests);
        return new VerificationOutcome(success, reason);
    }

    private static string? RestoreUnexpectedSourceMutations(
        GitRepository repository,
        IReadOnlyDictionary<string, byte[]> expectedBytes)
    {
        string? firstMutation = null;
        foreach ((string path, byte[] expected) in expectedBytes)
        {
            string fullPath = PathRules.ResolveInsideRoot(repository.Root, path, "candidate path");
            byte[] actual = ReadBytes(fullPath);
            if (!actual.AsSpan().SequenceEqual(expected))
            {
                firstMutation ??= path;
                WriteBytes(fullPath, expected);
            }
        }

        return firstMutation;
    }

    private static byte[] ApplyTextEdit(byte[] sourceBytes, int start, int length)
    {
        string text = DecodeUtf8(sourceBytes, out bool bom);
        if (start < 0 || length <= 0 || start + length > text.Length)
        {
            throw new ContractException("Candidate edit span is outside the current source.");
        }

        string edited = text.Remove(start, length);
        byte[] content = new UTF8Encoding(false, true).GetBytes(edited);
        if (!bom)
        {
            return content;
        }

        return [0xEF, 0xBB, 0xBF, .. content];
    }

    private static string DecodeUtf8(byte[] bytes, out bool bom)
    {
        bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        ReadOnlySpan<byte> content = bom ? bytes.AsSpan(3) : bytes;
        try
        {
            return new UTF8Encoding(false, true).GetString(content);
        }
        catch (DecoderFallbackException ex)
        {
            throw new ContractException($"Candidate source is not valid UTF-8: {ex.Message}");
        }
    }

    private static byte[] ReadBytes(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InfrastructureException($"Could not read '{path}'.", ex);
        }
    }

    private static void WriteBytes(string path, byte[] bytes)
    {
        try
        {
            File.WriteAllBytes(path, bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InfrastructureException($"Could not write '{path}'.", ex);
        }
    }

    private static void RestoreFiles(GitRepository repository, IReadOnlyDictionary<string, byte[]> files)
    {
        foreach ((string path, byte[] bytes) in files)
        {
            WriteBytes(PathRules.ResolveInsideRoot(repository.Root, path, "candidate path"), bytes);
        }
    }

    private static void ResetDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }

            Directory.CreateDirectory(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InfrastructureException($"Could not prepare verification directory '{path}'.", ex);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"warning: could not remove verification directory '{path}': {ex.Message}");
        }
    }

    private static string RequiredString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
        {
            throw new ContractException($"{name} must be a string.");
        }

        return value.GetString()!;
    }

    private static RevertedCandidateResult CreateRevertedCandidate(Candidate candidate, string reason) => new()
    {
        CandidateId = candidate.CandidateId,
        Path = candidate.Path,
        TestFqns = candidate.Owner.TestFqns.Order(StringComparer.Ordinal).ToList(),
        Reason = reason,
    };

    private static ApplyResult EmptyResult(string sourceCommit, string manifestDigest) => new()
    {
        SourceCommit = sourceCommit,
        ManifestDigest = manifestDigest,
        HasChanges = false,
    };

    private static string CreatePrBody(
        Manifest manifest,
        IReadOnlyList<Candidate> retained,
        IReadOnlyList<RevertedCandidateResult> reverted)
    {
        StringBuilder body = new();
        body.Append("<!-- unskip-closed-tests:v1;source=");
        body.Append(manifest.SourceCommit);
        body.Append(";manifest=");
        body.Append(manifest.ManifestDigest);
        body.AppendLine(" -->");
        body.AppendLine();
        body.AppendLine("## Verified unskips");
        body.AppendLine();
        foreach (Candidate candidate in retained)
        {
            body.Append("- `");
            body.Append(candidate.Path);
            body.Append("` — ");
            body.Append(string.Join(", ", candidate.Owner.TestFqns.Select(static fqn => $"`{fqn}`")));
            body.Append(" (");
            body.Append(string.Join(", ", candidate.CanonicalIssueReferences.Select(static reference =>
                $"[{reference.Canonical}]({reference.Url})")));
            body.AppendLine(")");
        }

        body.AppendLine();
        body.AppendLine("Each retained edit was verified independently by the configured trusted command and exact TRX FQN mapping.");
        if (reverted.Count > 0)
        {
            body.AppendLine();
            body.AppendLine($"The helper reverted {reverted.Count} candidate(s) that did not satisfy verification.");
        }

        return body.ToString().TrimEnd();
    }
}
