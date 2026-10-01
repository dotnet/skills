using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace UnskipClosedTests.Tool;

internal static class IssueResolver
{
    private static readonly HashSet<string> StructuralDeferrals =
    [
        "no_enumerated_tests",
        "class_is_nested",
        "class_is_partial",
        "class_has_base_types",
        "duplicate_type_declarations",
        "ambiguous_test_fqns",
        "generated_declaration",
    ];

    public static async Task<Manifest> ResolveAsync(Manifest input, string? evidencePath)
    {
        ManifestValidator.Validate(input);
        Manifest manifest = Clone(input);
        IReferenceEvidenceProvider provider = evidencePath is null
            ? new GitHubReferenceEvidenceProvider()
            : FixtureReferenceEvidenceProvider.Load(evidencePath);

        Dictionary<string, EvidenceReference> cache = new(StringComparer.Ordinal);
        foreach (Candidate candidate in manifest.Candidates)
        {
            List<string> deferrals = candidate.Decision.Deferrals
                .Where(StructuralDeferrals.Contains)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();
            List<IssueReference> resolvedReferences = [];
            foreach (IssueReference reference in candidate.CanonicalIssueReferences)
            {
                if (!cache.TryGetValue(reference.Canonical, out EvidenceReference? evidence))
                {
                    evidence = await provider.GetAsync(reference);
                    cache.Add(reference.Canonical, evidence);
                }

                IssueReference resolved = ResolveReference(reference, evidence);
                resolvedReferences.Add(resolved);
                if (!resolved.Eligibility)
                {
                    deferrals.Add($"reference_not_eligible:{resolved.Canonical}:{resolved.StateReason}");
                }
            }

            candidate.CanonicalIssueReferences = resolvedReferences
                .OrderBy(static reference => reference.Canonical, StringComparer.Ordinal)
                .ToList();
            if (candidate.Owner.TestFqns.Count == 0 && !deferrals.Contains("no_enumerated_tests", StringComparer.Ordinal))
            {
                deferrals.Add("no_enumerated_tests");
            }

            candidate.Decision = new CandidateDecision
            {
                Eligible = deferrals.Count == 0 &&
                    candidate.CanonicalIssueReferences.Count > 0 &&
                    candidate.CanonicalIssueReferences.All(static reference => reference.Eligibility),
                Deferrals = deferrals.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList(),
            };
        }

        manifest.ManifestDigest = "";
        manifest.ManifestDigest = JsonSupport.ManifestDigest(manifest);
        return manifest;
    }

    private static IssueReference ResolveReference(IssueReference original, EvidenceReference evidence)
    {
        if (!string.Equals(evidence.Canonical, original.Canonical, StringComparison.Ordinal))
        {
            throw new ContractException(
                $"Evidence canonical '{evidence.Canonical}' does not match requested reference '{original.Canonical}'.");
        }

        string kind = NormalizeKind(evidence.Kind.Length == 0 ? original.Kind : evidence.Kind);
        if (!evidence.Accessible)
        {
            return Copy(original, kind, false, "inaccessible", "inaccessible", null);
        }

        if (kind == "unknown")
        {
            return Copy(original, kind, false, "unknown", "unknown_reference_kind", null);
        }

        string state = evidence.State.ToLowerInvariant();
        string stateReason = evidence.StateReason.ToLowerInvariant();
        if (kind == "issue")
        {
            bool eligible = state == "closed" && stateReason == "completed";
            string reason = eligible
                ? "completed"
                : state == "open"
                    ? "open"
                    : stateReason == "not_planned"
                        ? "not_planned"
                        : "not_completed";
            return Copy(original, kind, eligible, state, reason, null);
        }

        bool merged = !string.IsNullOrWhiteSpace(evidence.MergedAt);
        if (merged && !DateTimeOffset.TryParse(
                evidence.MergedAt,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out _))
        {
            throw new ContractException($"Pull request evidence '{evidence.Canonical}' has malformed merged_at.");
        }

        return Copy(
            original,
            kind,
            merged,
            state.Length == 0 ? (merged ? "closed" : "unknown") : state,
            merged ? "merged" : "not_merged",
            evidence.MergedAt);
    }

    private static IssueReference Copy(
        IssueReference original,
        string kind,
        bool eligible,
        string state,
        string stateReason,
        string? mergedAt)
    {
        string pathKind = kind == "pull_request" ? "pull" : "issues";
        return new IssueReference
        {
            Kind = kind,
            Owner = original.Owner,
            Repo = original.Repo,
            Number = original.Number,
            Canonical = original.Canonical,
            Url = $"https://github.com/{original.Owner}/{original.Repo}/{pathKind}/{original.Number}",
            Eligibility = eligible,
            State = state,
            StateReason = stateReason,
            MergedAt = mergedAt,
        };
    }

    private static string NormalizeKind(string kind) => kind.ToLowerInvariant() switch
    {
        "issue" => "issue",
        "pull" or "pr" or "pull_request" => "pull_request",
        "unknown" or "" => "unknown",
        _ => throw new ContractException($"Unsupported evidence kind '{kind}'."),
    };

    private static Manifest Clone(Manifest input)
    {
        string json = JsonSerializer.Serialize(input, JsonSupport.Options);
        return JsonSerializer.Deserialize<Manifest>(json, JsonSupport.Options)
            ?? throw new InfrastructureException("Could not clone manifest.");
    }

    private interface IReferenceEvidenceProvider
    {
        Task<EvidenceReference> GetAsync(IssueReference reference);
    }

    private sealed class FixtureReferenceEvidenceProvider(
        IReadOnlyDictionary<string, EvidenceReference> references) : IReferenceEvidenceProvider
    {
        public static FixtureReferenceEvidenceProvider Load(string path)
        {
            using JsonDocument document = JsonSupport.ReadDocument(path);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new ContractException("GitHub evidence root must be an object.");
            }

            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (property.Name is not ("schema_version" or "references"))
                {
                    throw new ContractException($"Unknown GitHub evidence property '{property.Name}'.");
                }
            }

            if (!root.TryGetProperty("schema_version", out JsonElement schema) ||
                schema.ValueKind != JsonValueKind.String ||
                schema.GetString() != "1")
            {
                throw new ContractException("GitHub evidence schema_version must be '1'.");
            }

            if (!root.TryGetProperty("references", out JsonElement referencesElement))
            {
                throw new ContractException("GitHub evidence references is required.");
            }

            Dictionary<string, EvidenceReference> references = new(StringComparer.Ordinal);
            if (referencesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in referencesElement.EnumerateArray())
                {
                    EvidenceReference evidence = ParseEvidence(item, null);
                    if (!references.TryAdd(evidence.Canonical, evidence))
                    {
                        throw new ContractException($"Duplicate GitHub evidence '{evidence.Canonical}'.");
                    }
                }
            }
            else if (referencesElement.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in referencesElement.EnumerateObject())
                {
                    EvidenceReference evidence = ParseEvidence(property.Value, property.Name);
                    if (!references.TryAdd(evidence.Canonical, evidence))
                    {
                        throw new ContractException($"Duplicate GitHub evidence '{evidence.Canonical}'.");
                    }
                }
            }
            else
            {
                throw new ContractException("GitHub evidence references must be an array or object.");
            }

            return new FixtureReferenceEvidenceProvider(references);
        }

        public Task<EvidenceReference> GetAsync(IssueReference reference)
        {
            if (references.TryGetValue(reference.Canonical, out EvidenceReference? evidence))
            {
                return Task.FromResult(evidence);
            }

            return Task.FromResult(new EvidenceReference
            {
                Canonical = reference.Canonical,
                Kind = reference.Kind,
                Accessible = false,
                State = "inaccessible",
                StateReason = "inaccessible",
            });
        }

        private static EvidenceReference ParseEvidence(JsonElement element, string? canonicalFromKey)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new ContractException("Each GitHub evidence entry must be an object.");
            }

            HashSet<string> allowed = ["canonical", "kind", "accessible", "state", "state_reason", "merged_at"];
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!allowed.Contains(property.Name))
                {
                    throw new ContractException($"Unknown GitHub evidence field '{property.Name}'.");
                }
            }

            string canonical = canonicalFromKey ?? RequiredString(element, "canonical");
            if (element.TryGetProperty("canonical", out JsonElement canonicalElement) &&
                (canonicalElement.ValueKind != JsonValueKind.String ||
                 !string.Equals(canonicalElement.GetString(), canonical, StringComparison.Ordinal)))
            {
                throw new ContractException("GitHub evidence canonical key and field do not match.");
            }

            string kind = RequiredString(element, "kind");
            bool accessible = true;
            if (element.TryGetProperty("accessible", out JsonElement accessibleElement))
            {
                if (accessibleElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    throw new ContractException("GitHub evidence accessible must be a boolean.");
                }

                accessible = accessibleElement.GetBoolean();
            }

            string state = OptionalString(element, "state");
            string stateReason = OptionalNullableString(element, "state_reason") ?? "";
            string? mergedAt = OptionalNullableString(element, "merged_at");
            if (accessible && kind.Equals("issue", StringComparison.OrdinalIgnoreCase) &&
                state.Length == 0)
            {
                throw new ContractException($"Accessible issue evidence '{canonical}' requires state.");
            }

            if (accessible && kind.Equals("issue", StringComparison.OrdinalIgnoreCase) &&
                state.Equals("closed", StringComparison.OrdinalIgnoreCase) &&
                stateReason.Length == 0)
            {
                throw new ContractException($"Closed issue evidence '{canonical}' requires state_reason.");
            }

            if (accessible && NormalizeKind(kind) == "pull_request" && state.Length == 0)
            {
                throw new ContractException($"Accessible pull request evidence '{canonical}' requires state.");
            }

            return new EvidenceReference
            {
                Canonical = canonical,
                Kind = kind,
                Accessible = accessible,
                State = state,
                StateReason = stateReason,
                MergedAt = mergedAt,
            };
        }

        private static string RequiredString(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
            {
                throw new ContractException($"GitHub evidence {name} must be a string.");
            }

            return value.GetString()!;
        }

        private static string OptionalString(JsonElement element, string name) =>
            element.TryGetProperty(name, out JsonElement value)
                ? value.ValueKind == JsonValueKind.String
                    ? value.GetString()!
                    : throw new ContractException($"GitHub evidence {name} must be a string.")
                : "";

        private static string? OptionalNullableString(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            return value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : throw new ContractException($"GitHub evidence {name} must be a string or null.");
        }
    }

    private sealed class GitHubReferenceEvidenceProvider : IReferenceEvidenceProvider
    {
        private readonly HttpClient _client;

        public GitHubReferenceEvidenceProvider()
        {
            string? token = Environment.GetEnvironmentVariable("GH_TOKEN");
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new InfrastructureException("GH_TOKEN is required when --github-evidence is not supplied.");
            }

            _client = new HttpClient
            {
                BaseAddress = new Uri("https://api.github.com/"),
                Timeout = TimeSpan.FromSeconds(30),
            };
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            _client.DefaultRequestHeaders.UserAgent.ParseAdd("unskip-closed-tests-tool/1");
            _client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            _client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        }

        public async Task<EvidenceReference> GetAsync(IssueReference reference)
        {
            try
            {
                using HttpResponseMessage issueResponse = await _client.GetAsync(
                    $"repos/{Uri.EscapeDataString(reference.Owner)}/{Uri.EscapeDataString(reference.Repo)}/issues/{reference.Number}");
                if (issueResponse.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
                {
                    if (issueResponse.Headers.TryGetValues("X-RateLimit-Remaining", out IEnumerable<string>? remaining) &&
                        remaining.Contains("0", StringComparer.Ordinal))
                    {
                        throw new InfrastructureException("GitHub API rate limit was exhausted.");
                    }

                    return Inaccessible(reference);
                }

                if (issueResponse.StatusCode == HttpStatusCode.Unauthorized)
                {
                    throw new InfrastructureException("GitHub authentication was rejected.");
                }

                if (!issueResponse.IsSuccessStatusCode)
                {
                    throw new InfrastructureException($"GitHub issue lookup returned {(int)issueResponse.StatusCode}.");
                }

                using JsonDocument issue = JsonDocument.Parse(await issueResponse.Content.ReadAsStreamAsync());
                JsonElement issueRoot = issue.RootElement;
                bool isPullRequest = issueRoot.TryGetProperty("pull_request", out _);
                if (!isPullRequest)
                {
                    return new EvidenceReference
                    {
                        Canonical = reference.Canonical,
                        Kind = "issue",
                        Accessible = true,
                        State = RequiredApiString(issueRoot, "state"),
                        StateReason = NullableApiString(issueRoot, "state_reason") ?? "",
                    };
                }

                using HttpResponseMessage pullResponse = await _client.GetAsync(
                    $"repos/{Uri.EscapeDataString(reference.Owner)}/{Uri.EscapeDataString(reference.Repo)}/pulls/{reference.Number}");
                if (!pullResponse.IsSuccessStatusCode)
                {
                    if (pullResponse.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
                    {
                        return Inaccessible(reference);
                    }

                    throw new InfrastructureException($"GitHub pull request lookup returned {(int)pullResponse.StatusCode}.");
                }

                using JsonDocument pull = JsonDocument.Parse(await pullResponse.Content.ReadAsStreamAsync());
                JsonElement pullRoot = pull.RootElement;
                return new EvidenceReference
                {
                    Canonical = reference.Canonical,
                    Kind = "pull_request",
                    Accessible = true,
                    State = RequiredApiString(pullRoot, "state"),
                    StateReason = "",
                    MergedAt = NullableApiString(pullRoot, "merged_at"),
                };
            }
            catch (InfrastructureException)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
            {
                throw new InfrastructureException($"GitHub lookup failed for {reference.Canonical}.", ex);
            }
        }

        private static EvidenceReference Inaccessible(IssueReference reference) => new()
        {
            Canonical = reference.Canonical,
            Kind = reference.Kind,
            Accessible = false,
            State = "inaccessible",
            StateReason = "inaccessible",
        };

        private static string RequiredApiString(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
            {
                throw new InfrastructureException($"GitHub response omitted string field '{name}'.");
            }

            return value.GetString()!;
        }

        private static string? NullableApiString(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out JsonElement value))
            {
                throw new InfrastructureException($"GitHub response omitted field '{name}'.");
            }

            return value.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String => value.GetString(),
                _ => throw new InfrastructureException($"GitHub response field '{name}' is malformed."),
            };
        }
    }
}
