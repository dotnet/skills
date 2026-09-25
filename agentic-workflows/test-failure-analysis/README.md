# Test Failure Analysis

Installs a provider-neutral Test Failure Analysis agentic workflow. A
repository-owned, deterministic CI collector normalizes test results into a
bounded GitHub Actions artifact; this package validates and sanitizes that
artifact, then asks a read-only analyst to report evidence-backed failures,
retries/flakes, hangs/timeouts, crashes, and meaningful duration regressions.

The workflow never downloads evidence from arbitrary URLs, executes evidence,
builds code, or runs tests. External CI systems must be normalized by the
consumer's collector before this workflow is invoked.

## Install

From the root of the consuming repository:

```powershell
gh aw add-wizard dotnet/skills/agentic-workflows/test-failure-analysis@main
```

Pin production installations to a release tag or commit SHA instead of `main`.
The package installs:

- `.github/workflows/test-failure-analysis.md`
- `.github/workflows/test-failure-analysis-fetch.md`
- `.github/workflows/test-failure-analysis-shared.md`
- `.github/agents/test-failure-analyst.agent.md`
- the generated `.github/workflows/test-failure-analysis.lock.yml`

After local customization, run:

```powershell
gh aw compile test-failure-analysis --strict
```

Commit both the installed Markdown source and generated `.lock.yml` in the
consuming repository. Pull compatible package updates with:

```powershell
gh aw update test-failure-analysis
gh aw compile test-failure-analysis --strict
```

Generated consumer lock files are intentionally not stored in this
distribution package.

## Architecture and producer contract

The consumer owns a conventional GitHub Actions workflow with two deterministic
stages:

1. **Collector** — reads repository/CI-specific results, normalizes them without
   AI, writes `metadata.json` plus JSON, JSONL, or normalized text evidence, and
   uploads one Actions artifact.
2. **Analyst** — calls the installed
   `.github/workflows/test-failure-analysis.lock.yml`, passing the artifact run
   ID/name and trusted scalar identity metadata.

Collectors may query any CI provider, but must do so before invoking this
workflow. They must not put credentials, raw executable content, archives, test
binaries, dumps, or arbitrary downloaded pages in the evidence artifact.

The fetch job restricts retrieval to the current repository and enforces:

- a 64 MiB compressed artifact limit;
- at most 200 ZIP entries and 100 selected evidence files;
- at most 4 MiB per selected file and 16 MiB selected in total;
- at most 5,000 structured records and 10,000 normalized text lines;
- relative paths whose segments use only letters, digits, `.`, `_`, and `-`,
  with no traversal, backslashes, control characters, or symlinks;
- only `.json`, `.jsonl`, and `.txt` files;
- exact repository, PR, phase, tested SHA, build identity, run ID, and optional
  source URL/summary-location matches;
- PR head freshness before and after collection. The analyst rechecks freshness
  immediately before any output.

Identity or freshness mismatches fail closed before agent activation.
Completeness markers in `metadata.json` are authoritative: incomplete final
evidence is reported as inconclusive, never clean.

The generated workflow keeps one read-only agent job; gh-aw's threat-detection
and safe-output jobs mediate all writes.

## Evidence schema

The artifact must contain a root `metadata.json`:

```json
{
  "schema_version": "1",
  "repository": "owner/repository",
  "pr_number": 123,
  "head_sha": "0123456789abcdef0123456789abcdef01234567",
  "tested_sha": "89abcdef0123456789abcdef0123456789abcdef",
  "build_identity": "ci-tests:987654321:attempt-2",
  "analysis_phase": "final",
  "source": {
    "run_id": 987654321,
    "run_url": "https://github.com/owner/repository/actions/runs/987654321",
    "summary_location": "artifact:test-evidence/summary.txt"
  },
  "completeness": {
    "complete": true,
    "reasons": [],
    "failures": "complete",
    "retries": "complete",
    "hangs": "complete",
    "crashes": "complete",
    "durations": "complete",
    "history": "partial"
  },
  "files": [
    "failures.jsonl",
    "retries.jsonl",
    "hangs.jsonl",
    "crashes.jsonl",
    "durations.jsonl",
    "summary.txt"
  ]
}
```

Category files may be JSON arrays/objects or one JSON object per JSONL line.
Normalized text is allowed only as supporting evidence. Records should use these
portable fields where applicable:

```json
{
  "category": "failure",
  "signature": "test:Namespace.Type.Method|assertion:expected-actual",
  "test_id": "Namespace.Type.Method",
  "outcome": "failed",
  "attempt": 1,
  "max_attempts": 2,
  "message": "Expected 42, actual 41",
  "stack": "Namespace.Type.Method() in tests/example.cs:line 17",
  "duration_seconds": 12.4,
  "baseline_seconds": 4.1,
  "baseline_samples": 20,
  "timestamp": "2026-09-25T12:00:00Z",
  "source_file": "tests/example.cs",
  "source_line": 17,
  "evidence_ref": "failures.jsonl:1"
}
```

Use `category` values `failure`, `retry`, `hang`, `timeout`, `crash`, or
`duration_regression`. A stable `signature` helps group equivalent findings.
Retry/flake records must preserve every observed attempt and final outcome.
Duration records must include a comparable baseline, sample count, and
measurement window. Missing history must be marked `partial` or `absent`; it
cannot support “no recurrence” claims.

For portable historical analysis, collectors should inspect no more than the
latest 8 comparable builds or 14 days, whichever is smaller, and select no more
than 200 matching results per build. If a provider cannot supply that window or
the collector truncates it, mark `history` as `partial` rather than implying the
failure did not recur.

## Example producer

This abbreviated workflow assumes `scripts/collect-test-evidence` is a
repository-specific deterministic normalizer. Pin third-party actions in
production according to repository policy.

```yaml
name: Test evidence

on:
  workflow_dispatch:
    inputs:
      pr-number:
        required: true
        type: string
      expected-head-sha:
        required: true
        type: string
      tested-sha:
        required: true
        type: string
      phase:
        required: true
        type: choice
        options: [preliminary, final]

jobs:
  collect:
    runs-on: ubuntu-latest
    permissions:
      contents: read
      pull-requests: read
    outputs:
      build-identity: ${{ steps.identity.outputs.value }}
    steps:
      - uses: actions/checkout@v6
      - id: identity
        run: echo "value=tests:${GITHUB_RUN_ID}:${GITHUB_RUN_ATTEMPT}" >> "$GITHUB_OUTPUT"
      - name: Normalize repository-specific test results
        env:
          REPOSITORY: ${{ github.repository }}
          PR_NUMBER: ${{ inputs.pr-number }}
          HEAD_SHA: ${{ inputs.expected-head-sha }}
          TESTED_SHA: ${{ inputs.tested-sha }}
          PHASE: ${{ inputs.phase }}
          BUILD_IDENTITY: ${{ steps.identity.outputs.value }}
          SOURCE_RUN_ID: ${{ github.run_id }}
          SOURCE_RUN_URL: ${{ github.server_url }}/${{ github.repository }}/actions/runs/${{ github.run_id }}
          SUMMARY_LOCATION: artifact:normalized-test-evidence/summary.txt
        run: scripts/collect-test-evidence --output test-evidence
      - uses: actions/upload-artifact@v7
        with:
          name: normalized-test-evidence
          path: test-evidence
          if-no-files-found: error
          retention-days: 3

  analyze:
    needs: collect
    uses: ./.github/workflows/test-failure-analysis.lock.yml
    permissions:
      actions: read
      contents: read
      issues: write
      pull-requests: write
      copilot-requests: write
    with:
      evidence-run-id: ${{ github.run_id }}
      evidence-artifact-name: normalized-test-evidence
      analysis-phase: ${{ inputs.phase }}
      pr-number: ${{ inputs.pr-number }}
      expected-head-sha: ${{ inputs.expected-head-sha }}
      expected-tested-sha: ${{ inputs.tested-sha }}
      expected-build-identity: ${{ needs.collect.outputs.build-identity }}
      trusted-comment-author: github-actions[bot]
      source-run-url: ${{ github.server_url }}/${{ github.repository }}/actions/runs/${{ github.run_id }}
      evidence-summary-location: artifact:normalized-test-evidence/summary.txt
      duration-regression-percent: 25
      duration-regression-minimum-seconds: 30
      duration-regression-minimum-baseline-samples: 5
```

For external test systems, the collector downloads and normalizes their results,
then uploads the bounded artifact to this repository's Actions run. The analyst
never contacts the external system.

## Provenance

The provider-neutral architecture and analysis lifecycle were generalized from
[`microsoft/testfx`'s `pipeline-test-triage.md`](https://github.com/microsoft/testfx/blob/eb6aa01e21637fa15f0000a1f041e263a6601108/.github/workflows/pipeline-test-triage.md)
at commit `eb6aa01e21637fa15f0000a1f041e263a6601108`. Provider-specific collection,
repository policy, and TestFX terminology were deliberately replaced by the
bounded producer contract documented above.
