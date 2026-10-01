namespace UnskipClosedTests.Tool;

internal static class ManifestValidator
{
    public static Manifest Read(string path)
    {
        Manifest manifest = JsonSupport.Read<Manifest>(path);
        Validate(manifest);
        return manifest;
    }

    public static void Validate(Manifest manifest)
    {
        if (manifest.SchemaVersion != "1")
        {
            throw new ContractException($"Unsupported manifest schema_version '{manifest.SchemaVersion}'.");
        }

        if (manifest.CandidateCount != manifest.Candidates.Count)
        {
            throw new ContractException("candidate_count does not match candidates.");
        }

        if (manifest.Repository.Split('/').Length != 2 ||
            manifest.SourceCommit.Length is not (40 or 64) ||
            manifest.GitObjectFormat is not ("sha1" or "sha256") ||
            manifest.ConfigDigest.Length != 64 ||
            manifest.ManifestDigest.Length != 64)
        {
            throw new ContractException("Manifest root identity fields are malformed.");
        }

        if (!string.Equals(JsonSupport.ManifestDigest(manifest), manifest.ManifestDigest, StringComparison.Ordinal))
        {
            throw new ContractException("manifest_digest does not match manifest content.");
        }

        HashSet<string> candidateIds = new(StringComparer.Ordinal);
        foreach (Candidate candidate in manifest.Candidates)
        {
            if (!candidateIds.Add(candidate.CandidateId))
            {
                throw new ContractException($"Duplicate candidate_id '{candidate.CandidateId}'.");
            }

            PathRules.ValidateRelativePath(candidate.Path, "candidate path");
            if (!candidate.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                candidate.CandidateId.Length != 64 ||
                candidate.StableOwnerId.Length != 64 ||
                candidate.SourceSha256.Length != 64 ||
                candidate.AttributeTextSha256.Length != 64 ||
                candidate.AttributeSpan.Start < 0 ||
                candidate.AttributeSpan.Length <= 0 ||
                candidate.Owner.ContainingTypes.Count == 0 ||
                candidate.Owner.TypeFqn.Length == 0 ||
                candidate.Owner.DeclarationId.Length == 0)
            {
                throw new ContractException($"Candidate '{candidate.CandidateId}' has malformed trusted identity fields.");
            }

            if (candidate.Owner.Kind is not ("method" or "class"))
            {
                throw new ContractException($"Candidate '{candidate.CandidateId}' has unsupported owner kind.");
            }

            if (candidate.CanonicalIssueReferences.Count == 0)
            {
                throw new ContractException($"Candidate '{candidate.CandidateId}' has no concrete issue references.");
            }

            if (candidate.Owner.TestFqns.Distinct(StringComparer.Ordinal).Count() != candidate.Owner.TestFqns.Count)
            {
                throw new ContractException($"Candidate '{candidate.CandidateId}' has duplicate test FQNs.");
            }
        }
    }
}
