using BlazorComponentReadiness.Validator.Contracts;
using BlazorComponentReadiness.Validator.IO;

namespace BlazorComponentReadiness.Validator.Evidence;

public static class EvidenceLedgerBuilder
{
    public static EvidenceSourceLedger BuildRepositoryLedger(
        RepositoryLedgerSubject subject,
        IEnumerable<EvidenceRecordDraft> records,
        IEvidenceHasher? hasher = null)
    {
        return BuildLedger(
            "repository",
            EvidenceIdentity.NormalizeRepositorySubject(subject),
            componentSubject: null,
            records,
            hasher);
    }

    public static EvidenceSourceLedger BuildComponentLedger(
        ExactAssessmentIdentity subject,
        IEnumerable<EvidenceRecordDraft> records,
        IEvidenceHasher? hasher = null)
    {
        return BuildLedger(
            "component",
            repositorySubject: null,
            EvidenceIdentity.NormalizeAssessment(subject),
            records,
            hasher);
    }

    public static EvidenceBundle BuildBundle(
        ExactAssessmentIdentity assessment,
        IReadOnlyList<EvidenceSourceLedger> sourceLedgers,
        IReadOnlyList<string> selectedEvidenceIds)
    {
        var normalizedAssessment = EvidenceIdentity.NormalizeAssessment(assessment);
        if (sourceLedgers.Count == 0)
        {
            throw new DeterministicValidationException(
                "EVID008: evidence bundle requires at least one source ledger.");
        }

        if (selectedEvidenceIds.Count == 0)
        {
            throw new DeterministicValidationException(
                "EVID008: evidence bundle selection must not be empty.");
        }

        var embedded = sourceLedgers
            .Select(ledger =>
            {
                EvidenceLedgerValidator.ValidateSourceLedger(ledger);
                EvidenceLedgerValidator.ValidateCompatibility(normalizedAssessment, ledger);
                return new EmbeddedSourceLedger(
                    CanonicalEvidenceJson.ComputeSourceLedgerSha256(ledger),
                    ledger);
            })
            .OrderBy(source => source.SourceLedgerSha256, StringComparer.Ordinal)
            .ToArray();

        var duplicateLedgers = embedded
            .GroupBy(source => source.SourceLedgerSha256, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateLedgers.Length > 0)
        {
            throw new DeterministicValidationException(
                "EVID008: duplicate source ledgers: " + string.Join(", ", duplicateLedgers));
        }

        var duplicateSelection = selectedEvidenceIds
            .GroupBy(identifier => identifier, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateSelection.Length > 0)
        {
            throw new DeterministicValidationException(
                "EVID008: duplicate selected evidence IDs: " +
                string.Join(", ", duplicateSelection));
        }

        var selection = new List<EvidenceSelection>(selectedEvidenceIds.Count);
        for (var index = 0; index < selectedEvidenceIds.Count; index++)
        {
            var identifier = selectedEvidenceIds[index];
            EvidenceIdentity.ValidateStableIdentifier(identifier);
            var matches = embedded
                .SelectMany(source => source.Ledger.Records
                    .Where(record => string.Equals(record.StableId, identifier, StringComparison.Ordinal))
                    .Select(_ => source))
                .ToArray();
            if (matches.Length != 1)
            {
                throw new DeterministicValidationException(
                    $"EVID008: selected evidence ID {identifier} resolves to " +
                    $"{matches.Length} source-ledger members; expected exactly one.");
            }

            selection.Add(new EvidenceSelection(
                index + 1,
                matches[0].SourceLedgerSha256,
                identifier));
        }

        var bundle = new EvidenceBundle(
            CanonicalEvidenceJson.EvidenceSchemaVersion,
            normalizedAssessment,
            embedded,
            selection);
        EvidenceLedgerValidator.ValidateBundle(bundle);
        return bundle;
    }

    private static EvidenceSourceLedger BuildLedger(
        string ledgerKind,
        RepositoryLedgerSubject? repositorySubject,
        ExactAssessmentIdentity? componentSubject,
        IEnumerable<EvidenceRecordDraft> records,
        IEvidenceHasher? hasher)
    {
        ArgumentNullException.ThrowIfNull(records);
        var identities = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var built = new Dictionary<string, EvidenceRecord>(StringComparer.Ordinal);
        foreach (var source in records)
        {
            var normalized = EvidenceIdentity.NormalizeRecordDraft(source);
            var preimage = CanonicalEvidenceJson.GetRecordIdentityPreimage(
                ledgerKind,
                repositorySubject,
                componentSubject,
                normalized);
            var identifier = CanonicalEvidenceJson.ComputeStableId(
                ledgerKind,
                repositorySubject,
                componentSubject,
                normalized,
                hasher);
            if (identities.TryGetValue(identifier, out var existingPreimage))
            {
                if (!existingPreimage.AsSpan().SequenceEqual(preimage))
                {
                    throw new DeterministicValidationException(
                        $"EVID003: stable evidence collision for {identifier}.");
                }

                continue;
            }

            identities.Add(identifier, preimage);
            built.Add(identifier, new EvidenceRecord(
                identifier,
                normalized.Claim,
                normalized.Applicability,
                normalized.Provenance,
                normalized.Supersedes));
        }

        if (built.Count == 0)
        {
            throw new DeterministicValidationException(
                "EVID008: source ledger requires at least one evidence record.");
        }

        var ledger = new EvidenceSourceLedger(
            CanonicalEvidenceJson.EvidenceSchemaVersion,
            ledgerKind,
            repositorySubject,
            componentSubject,
            built.Values.OrderBy(record => record.StableId, StringComparer.Ordinal).ToArray());
        EvidenceLedgerValidator.ValidateSourceLedger(ledger);
        return ledger;
    }
}
