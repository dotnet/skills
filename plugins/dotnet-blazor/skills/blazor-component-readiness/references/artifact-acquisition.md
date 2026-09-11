# Artifact acquisition

Use this before assessing a distributed package. Transport failure must not redefine a release as
source-only.

## Published package

1. Record the configured public package source and requested ID/version.
2. Discover the latest stable only as a draft default; ask the owner to confirm the exact version.
3. Use the package source's standard metadata and content endpoints to confirm and retrieve the
   exact original nupkg. Use a bounded alternate endpoint only when the primary route fails.
4. Record every attempt with subject, locator, retrieval method, result, and bounded detail.
   Allowed results are `succeeded`, `not-found`, `access-denied`, `unavailable`,
   `invalid-content`, and `network-failure`.
5. Hash before extraction. Canonical ID/version come from the nuspec inside those exact bytes.
6. A package cache is acceptable only when origin and exact original bytes are established. A local
   rebuild is not the published artifact.

Network, proxy, authentication, DNS, or tool failure is a retrieval result, not a product `gap`.
Continue with only claims the retained evidence can support; affected applicable rows are
`not tested` or `owner evidence required`.

## Release candidate

Use the owner-supplied local `.nupkg`, record acquisition as `release-candidate`, bind its SHA-256,
and start a distinct package root when bytes change even if the semantic version does not.

## Source archive capture

When the selected source is an already retained GitHub archive, extraction is an acquisition step,
not a checkout. For a GitHub codeload archive, record the repository URI, exact commit, archive
locator, top-level entry prefix, mapping rationale, and confidence. First inventory actual regular
archive entries, then choose relevant entries by their numeric IDs; the inventory is structural
source-file selection, not a component or library assessment inventory:

```text
<launcher> source inventory-archive --root <root> --archive <root-relative.tar.gz> \
  --source-root <root-relative-extracted-repository-root> --archive-prefix <github-top-level-directory> \
  --archive-format tar.gz --output <new-inventory.json>
```

`source-root` is the actual local repository root where each repository-relative `source_path`
exists; it is not necessarily the extraction destination. `archive-prefix` applies only to archive
entry names. For example, if the archive contains `bundle/src/a.txt`, then
`tar -xzf archive.tar.gz -C extracted` is captured with
`--source-root extracted/bundle --archive-prefix bundle`. If extraction uses
`--strip-components=1`, use `--source-root extracted` but still keep `--archive-prefix bundle`,
because the retained archive bytes did not change. Do not flatten the tree with `unzip -j`.

For a large retained inventory, an optional read-only `jq` projection makes the actual numeric IDs
and repository-relative paths inspectable without rewriting the machine-owned JSON:

```bash
INVENTORY="<retained-inventory.json>"
LISTING="<disposable-inspection-listing.tsv>"
(set -C; jq -r '.entries[] | [.id, .source_path] | @tsv' "$INVENTORY" > "$LISTING")
```

Use a bounded file-view range to inspect `$LISTING`, then pass only actual IDs from that listing to
`capture-inventory`, which still revalidates the original inventory. Set `$LISTING` to a separate
new file; the subshell refuses to overwrite an existing file. The listing is disposable, not an
inventory replacement or evidence of completeness, provenance, or component/library classification.
If `jq` is unavailable, use an available JSON/file viewer instead; no installation is required.

Use the retained inventory's actual IDs with:

```text
<launcher> source capture-inventory --root <root> --inventory <inventory.json> \
  --entry-id <1-based-entry-id> --repository-uri <repository-uri> --source-commit <40-hex-commit> \
  --source-mapping "<mapping-rationale>" --source-confidence <low|medium|high> \
  --acquisition-locator <codeload-archive-url> \
  --output <new-receipt.json>
```

Read the retained inventory and receipt, not launcher stdout. Repeat `--entry-id` for each
selected file. IDs are local inventory references, not evidence IDs. The inventory is machine-owned:
do not edit or reformat it. Inventory mappings are recomputed against the retained archive before
capture; do not guess filenames or reconstruct paths manually.
`declared_source` records supplied provenance;
`archive` and `source_artifacts` record observed hashes and selected-file correspondence. The
helper never invokes Git, extracts bytes, executes archive contents, or establishes remote commit
truth. It does not establish complete project/import closure. Keep the receipt outside the extracted
source tree. ZIP archives use `--archive-format zip`; `--expected-sha256` can bind an independently
retained archive digest. Existing `source capture-archive` remains available when exact source paths
are already known.

Use the same declared repository/commit/mapping/confidence in `inputs candidates init`. For each
captured file, pass its `source_path` and `content_path` to
`inputs candidates add-source-artifact --input <previous-candidate> --source-path <source_path> --path <content_path> --output <new-candidate>`.
Do not copy receipt JSON or hashes into candidates; discovery computes the final digests. Retain
actual retrieval attempts separately; the helper does not perform or attest a download.

## Minimum package inspection

Persist the canonical six-field package identity before inspecting its contents:

```text
<launcher> package inspect --nupkg <exact.nupkg> --output <output>/package-inspection.json
```

Read that retained JSON artifact rather than redirecting or filtering launcher stdout, because
launcher build diagnostics can precede command output.

- nuspec identity, license, repository, authors, project, and source-commit metadata;
- files, target frameworks, dependencies, bundled scripts/styles/fonts/themes/notices;
- every shipped managed assembly, keeping strong-name, Authenticode, and package signatures
  separate;
- SBOM/provenance correspondence to the exact final package digest;
- source mapping to a reachable exact commit when source is available.

Input schema 1 records each captured source file in `source_artifacts` with its canonical
repository-relative `source_path`, local `content_path`, and exact SHA-256. Confirmation re-reads
the local capture. Component and unified evidence may select only confirmed source artifacts that
also appear in that component's `allowed_source_paths`; package evidence may use any confirmed
repository-wide source artifact.

For a source-available component, build an implementation closure rather than selecting only the
public wrapper. Include the component API/wrapper, inherited base/runtime types, referenced browser
interop handlers and modules, package-owned style/theme assets, and the component tests and samples.
Record an exact blocker for any missing category. In a blinded comparison, bind those categories to
the schema-v2 coverage surfaces before worker launch.

Selected evidence is re-bound during every assessment validation:

- official documentation requires the exact confirmed URL and content digest;
- package metadata requires the exact nupkg or recomputed entry digest, plus the matching captured
  package-source digest when that entry was retained separately;
- owner inputs require exact basename, digest, and either
  `owner-supplied-internal-evidence` or `owner-supplied-public-evidence`;
- source evidence requires exact `source:<source_path>` and captured digest.

Runtime observations and owner declarations remain claim-bounded. Failed source retrieval is
`unresolved`, never evidence that a product is closed source.

## Original library source closure

Before library analyzer, trim, or AOT checks, acquire and record the original library source
closure for the exact package identity: repository/archive provenance, source revision, and the
actual library project or solution. A synthetic consumer that references the package is not the
reviewed library target. If the original project or closure is unavailable, record the bounded
missing input and keep the corresponding result incomplete. This package-check prerequisite
does not require component selection, library inventory or worker orchestration.

## Offline release facts

The optional retained-input procedure now lives in [offline release facts](offline-release-facts.md).
Read it before invoking `release facts`; it is not a prerequisite for ordinary acquisition.

## Resource limits and cleanup

Honor validator ceilings: 256 MiB nupkg, 1 MiB nuspec, 4 MiB authored ledger, 64 MiB serialized
artifact, and at most 32 supplemental evidence inputs totaling 64 MiB.

Extract and probe outside the reviewed repository. Remove disposable extraction/probe trees and raw
logs after retaining bounded commands, results, relevant snippets, and digests.
