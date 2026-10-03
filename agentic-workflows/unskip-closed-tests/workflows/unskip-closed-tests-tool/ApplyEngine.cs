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
        string? evidencePath,
        string? verificationEvidenceDirectory = null)
    {
        Manifest requestedManifest = ManifestValidator.Read(manifestPath);
        if (!string.Equals(requestedManifest.ConfigDigest, ConfigLoader.Digest(config), StringComparison.Ordinal))
        {
            throw new ContractException("Apply config does not match manifest config_digest.");
        }

        List<string> selectedIds = ReadAgentSelection(
            agentOutputPath,
            requestedManifest.ManifestDigest,
            "apply_verified_unskips");
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
        List<string> retained = [];
        List<RevertedCandidateResult> reverted = [];
        HashSet<string> revertedIds = new(StringComparer.Ordinal);

        void RevertCandidate(Candidate candidate, string reason)
        {
            if (revertedIds.Add(candidate.CandidateId))
            {
                reverted.Add(CreateRevertedCandidate(candidate, reason));
            }
        }

        string workRoot = Path.Combine(
            repository.MetadataDirectory(),
            "unskip-closed-tests",
            requestedManifest.ManifestDigest);
        Manifest finalEligibility = freshResolved;
        try
        {
            ResetDirectory(workRoot);
            foreach (SourceEdit edit in edits)
            {
                Dictionary<string, byte[]> isolatedBytes = BuildEditedBytes(originalBytes, [edit]);
                RestoreFiles(repository, isolatedBytes);

                VerificationOutcome outcome = await VerifyCandidateAsync(
                    repository,
                    config,
                    requestedManifest,
                    edit.Candidate,
                    workRoot);
                string? mutation = RestoreUnexpectedSourceMutations(repository, isolatedBytes);
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
                    RevertCandidate(edit.Candidate, outcome.Reason);
                }
            }
            RestoreFiles(repository, originalBytes);

            while (retained.Count > 0)
            {
                HashSet<string> retainedSet = retained.ToHashSet(StringComparer.Ordinal);
                finalEligibility = await IssueResolver.ResolveAsync(freshInventory, evidencePath);
                HashSet<string> eligibleIds = finalEligibility.Candidates
                    .Where(static candidate => candidate.Decision.Eligible)
                    .Select(static candidate => candidate.CandidateId)
                    .ToHashSet(StringComparer.Ordinal);
                List<string> expired = retained
                    .Where(candidateId => !eligibleIds.Contains(candidateId))
                    .ToList();
                if (expired.Count > 0)
                {
                    foreach (string candidateId in expired)
                    {
                        RevertCandidate(candidates[candidateId], "eligibility_changed_after_verification");
                    }
                    retained = retained.Except(expired, StringComparer.Ordinal).ToList();
                    RestoreFiles(repository, originalBytes);
                    continue;
                }

                List<SourceEdit> retainedEdits = edits
                    .Where(edit => retainedSet.Contains(edit.Candidate.CandidateId))
                    .ToList();
                Dictionary<string, byte[]> finalBytes = BuildEditedBytes(originalBytes, retainedEdits);
                RestoreFiles(repository, finalBytes);

                List<string> failed = [];
                foreach (SourceEdit edit in retainedEdits)
                {
                    VerificationOutcome outcome = await VerifyCandidateAsync(
                        repository,
                        config,
                        requestedManifest,
                        edit.Candidate,
                        workRoot);
                    string? mutation = RestoreUnexpectedSourceMutations(repository, finalBytes);
                    if (mutation is not null)
                    {
                        outcome = new VerificationOutcome(false, $"verification_mutated_source:{mutation}");
                    }

                    if (!outcome.Success)
                    {
                        failed.Add(edit.Candidate.CandidateId);
                        RevertCandidate(edit.Candidate, $"final_set:{outcome.Reason}");
                    }
                }

                if (failed.Count > 0)
                {
                    retained = retained.Except(failed, StringComparer.Ordinal).ToList();
                    RestoreFiles(repository, originalBytes);
                    continue;
                }

                Manifest postVerificationEligibility =
                    await IssueResolver.ResolveAsync(freshInventory, evidencePath);
                HashSet<string> postVerificationEligibleIds = postVerificationEligibility.Candidates
                    .Where(static candidate => candidate.Decision.Eligible)
                    .Select(static candidate => candidate.CandidateId)
                    .ToHashSet(StringComparer.Ordinal);
                List<string> postVerificationExpired = retained
                    .Where(candidateId => !postVerificationEligibleIds.Contains(candidateId))
                    .ToList();
                if (postVerificationExpired.Count > 0)
                {
                    foreach (string candidateId in postVerificationExpired)
                    {
                        RevertCandidate(candidates[candidateId], "eligibility_changed_after_verification");
                    }
                    retained = retained.Except(postVerificationExpired, StringComparer.Ordinal).ToList();
                    RestoreFiles(repository, originalBytes);
                    continue;
                }

                finalEligibility = postVerificationEligibility;
                if (verificationEvidenceDirectory is not null)
                {
                    WriteVerificationEvidence(
                        workRoot,
                        verificationEvidenceDirectory,
                        retained,
                        agentOutputPath);
                }
                break;
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
        return CreateAuthorizedResult(requestedManifest, retainedCandidates, reverted);
    }


public static async Task<ApplyResult> AuthorizeAsync(
    string requestedRoot,
    ToolConfig config,
    string manifestPath,
    string agentOutputPath,
    string verificationEvidenceDirectory,
    string? evidencePath)
{
    Manifest requestedManifest = ManifestValidator.Read(manifestPath);
    if (!string.Equals(requestedManifest.ConfigDigest, ConfigLoader.Digest(config), StringComparison.Ordinal))
    {
        throw new ContractException("Authorize config does not match manifest config_digest.");
    }

    List<string> selectedIds = ReadAgentSelection(
        agentOutputPath,
        requestedManifest.ManifestDigest,
        "apply_verified_unskips");
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
        throw new ContractException("Authorize revalidation did not reproduce the trusted manifest.");
    }

    Dictionary<string, Candidate> candidates = freshResolved.Candidates
        .ToDictionary(static candidate => candidate.CandidateId, StringComparer.Ordinal);
    List<Candidate> retained = [];
    List<RevertedCandidateResult> reverted = [];
    foreach (string candidateId in selectedIds)
    {
        if (!candidates.TryGetValue(candidateId, out Candidate? candidate) ||
            !candidate.Decision.Eligible)
        {
            throw new ContractException(
                $"Authorize candidate_id '{candidateId}' is unknown or no longer eligible.");
        }

        string candidateDirectory = Path.Combine(
            Path.GetFullPath(verificationEvidenceDirectory),
            "final",
            candidateId);
        string requestPath = Path.Combine(candidateDirectory, "request.json");
        if (!File.Exists(requestPath))
        {
            reverted.Add(CreateRevertedCandidate(candidate, "missing_final_verification_evidence"));
            continue;
        }

        ApplyRequest request = JsonSupport.Read<ApplyRequest>(requestPath);
        List<VerificationTest> expectedTests = candidate.Owner.TestFqns
            .Order(StringComparer.Ordinal)
            .Select((fqn, index) => new VerificationTest
            {
                Fqn = fqn,
                SourcePath = candidate.Path,
                ResultFile = Path.Combine(candidateDirectory, $"{index:D4}.trx"),
            })
            .ToList();
        if (request.SchemaVersion != "1" ||
            request.Candidate.CandidateId != candidateId ||
            request.Repository != requestedManifest.Repository ||
            request.SourceCommit != requestedManifest.SourceCommit ||
            request.Tests.Count != expectedTests.Count ||
            !request.Tests.Zip(expectedTests).All(static pair =>
                pair.First.Fqn == pair.Second.Fqn &&
                pair.First.SourcePath == pair.Second.SourcePath &&
                Path.GetFileName(pair.First.ResultFile) == Path.GetFileName(pair.Second.ResultFile)))
        {
            throw new ContractException(
                $"Authorize evidence request for '{candidateId}' does not match the trusted manifest.");
        }

        (bool success, string reason) = TrxVerifier.Verify(expectedTests);
        if (success)
        {
            retained.Add(candidate);
        }
        else
        {
            reverted.Add(CreateRevertedCandidate(candidate, $"authorization:{reason}"));
        }
    }

    Manifest finalEligibility = await IssueResolver.ResolveAsync(freshInventory, evidencePath);
    HashSet<string> finallyEligible = finalEligibility.Candidates
        .Where(static candidate => candidate.Decision.Eligible)
        .Select(static candidate => candidate.CandidateId)
        .ToHashSet(StringComparer.Ordinal);
    foreach (Candidate candidate in retained.Where(
                 candidate => !finallyEligible.Contains(candidate.CandidateId)).ToList())
    {
        reverted.Add(CreateRevertedCandidate(
            candidate,
            "eligibility_changed_during_authorization"));
        retained.Remove(candidate);
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

    retained = retained
        .OrderBy(static candidate => candidate.Path, StringComparer.Ordinal)
        .ThenBy(static candidate => candidate.AttributeSpan.Start)
        .ToList();
    return CreateAuthorizedResult(requestedManifest, retained, reverted);
}

    public static async Task MaterializeAsync(
        string requestedRoot,
        ToolConfig config,
        string manifestPath,
        string resultPath,
        string? evidencePath)
    {
        Manifest requestedManifest = ManifestValidator.Read(manifestPath);
        if (!string.Equals(requestedManifest.ConfigDigest, ConfigLoader.Digest(config), StringComparison.Ordinal))
        {
            throw new ContractException("Materialize config does not match manifest config_digest.");
        }

        ApplyResult result = JsonSupport.Read<ApplyResult>(resultPath);
        if (!result.HasChanges ||
            result.SourceCommit != requestedManifest.SourceCommit ||
            result.ManifestDigest != requestedManifest.ManifestDigest ||
            result.RetainedCandidates.Count == 0)
        {
            throw new ContractException("Materialize result does not authorize verified changes.");
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
            throw new ContractException("Materialize revalidation did not reproduce the trusted manifest.");
        }

        Dictionary<string, Candidate> candidates = freshResolved.Candidates
            .ToDictionary(static candidate => candidate.CandidateId, StringComparer.Ordinal);
        if (result.RetainedCandidates.Select(static candidate => candidate.CandidateId)
            .Distinct(StringComparer.Ordinal).Count() != result.RetainedCandidates.Count)
        {
            throw new ContractException("Materialize result contains duplicate retained candidate IDs.");
        }

        List<SourceEdit> edits = [];
        foreach (RetainedCandidateResult retained in result.RetainedCandidates)
        {
            if (!candidates.TryGetValue(retained.CandidateId, out Candidate? candidate) ||
                !candidate.Decision.Eligible ||
                candidate.Path != retained.Path ||
                !candidate.Owner.TestFqns.Order(StringComparer.Ordinal)
                    .SequenceEqual(retained.TestFqns.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            {
                throw new ContractException(
                    $"Materialize retained candidate '{retained.CandidateId}' does not match the trusted manifest.");
            }

            edits.Add(CreateEdit(repository, config, candidate));
        }

        edits = edits
            .OrderBy(static edit => edit.Path, StringComparer.Ordinal)
            .ThenByDescending(static edit => edit.Start)
            .ToList();
        RejectOverlappingEdits(edits);
        List<string> changedPaths = edits.Select(static edit => edit.Path)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (!changedPaths.SequenceEqual(result.ChangedPaths.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw new ContractException("Materialize changed_paths do not match retained candidates.");
        }

        Dictionary<string, byte[]> originalBytes = changedPaths.ToDictionary(
            static path => path,
            path => ReadBytes(PathRules.ResolveInsideRoot(repository.Root, path, "candidate path")),
            StringComparer.Ordinal);
        RestoreFiles(repository, BuildEditedBytes(originalBytes, edits));
    }

    private static List<string> ReadAgentSelection(
        string path,
        string expectedManifestDigest,
        string expectedType)
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

        List<JsonElement> matchingItems = items
            .Where(item =>
                item.ValueKind == JsonValueKind.Object &&
                item.TryGetProperty("type", out JsonElement typeElement) &&
                typeElement.ValueKind == JsonValueKind.String &&
                typeElement.GetString() == expectedType)
            .ToList();
        if (matchingItems.Count != 1)
        {
            throw new ContractException(
                $"Agent output must contain exactly one '{expectedType}' output item.");
        }

        JsonElement item = matchingItems[0];
        HashSet<string> allowed = ["type", "manifest_digest", "candidate_ids_json"];
        foreach (JsonProperty property in item.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
            {
                throw new ContractException($"Unknown apply_verified_unskips field '{property.Name}'.");
            }
        }

        string type = RequiredString(item, "type");
        if (type != expectedType)
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
            string.IsNullOrWhiteSpace(candidate.AttributeType) ||
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
        startInfo.Environment.Remove("GH_TOKEN");
        startInfo.Environment.Remove("GITHUB_TOKEN");

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

    private static Dictionary<string, byte[]> BuildEditedBytes(
        IReadOnlyDictionary<string, byte[]> originalBytes,
        IEnumerable<SourceEdit> edits)
    {
        Dictionary<string, byte[]> editedBytes = originalBytes.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.ToArray(),
            StringComparer.Ordinal);
        foreach (SourceEdit edit in edits)
        {
            editedBytes[edit.Path] = ApplyTextEdit(
                editedBytes[edit.Path],
                edit.Start,
                edit.Length);
        }

        return editedBytes;
    }

    private static ApplyResult CreateAuthorizedResult(
        Manifest manifest,
        IReadOnlyList<Candidate> retainedCandidates,
        IReadOnlyList<RevertedCandidateResult> reverted)
    {
        int testCount = retainedCandidates.Sum(static candidate => candidate.Owner.TestFqns.Count);
        return new ApplyResult
        {
            SourceCommit = manifest.SourceCommit,
            ManifestDigest = manifest.ManifestDigest,
            RetainedCandidates = retainedCandidates
                .Select(static candidate => new RetainedCandidateResult
                {
                    CandidateId = candidate.CandidateId,
                    Path = candidate.Path,
                    TestFqns = candidate.Owner.TestFqns.Order(StringComparer.Ordinal).ToList(),
                })
                .OrderBy(static candidate => candidate.CandidateId, StringComparer.Ordinal)
                .ToList(),
            RevertedCandidates = reverted
                .OrderBy(static item => item.CandidateId, StringComparer.Ordinal)
                .ToList(),
            ChangedPaths = retainedCandidates
                .Select(static candidate => candidate.Path)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList(),
            HasChanges = true,
            PrTitle = testCount == 1
                ? "[unskip-closed-tests] Unskip test for completed GitHub work item"
                : $"[unskip-closed-tests] Unskip {testCount} tests for completed GitHub work items",
            PrBody = CreatePrBody(manifest, retainedCandidates, reverted),
        };
    }

    private static void WriteVerificationEvidence(
        string workRoot,
        string outputDirectory,
        IReadOnlyList<string> retainedCandidateIds,
        string agentOutputPath)
    {
        ResetDirectory(outputDirectory);
        string finalDirectory = Path.Combine(outputDirectory, "final");
        Directory.CreateDirectory(finalDirectory);
        foreach (string candidateId in retainedCandidateIds)
        {
            CopyDirectory(
                Path.Combine(workRoot, candidateId),
                Path.Combine(finalDirectory, candidateId));
        }

        File.Copy(
            agentOutputPath,
            Path.Combine(outputDirectory, "agent-output.json"),
            overwrite: false);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: false);
        }

        foreach (string directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(
                directory,
                Path.Combine(destination, Path.GetFileName(directory)));
        }
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
