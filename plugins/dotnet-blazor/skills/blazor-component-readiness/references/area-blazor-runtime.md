# Blazor runtime behavior

Applies to `BEQ-*`.

## Disposable component probes

Apply the skill's [assessed-code execution prerequisite](../SKILL.md#assessed-code-execution-prerequisite)
before restore, build, startup or runtime/browser probes; static inspection is a separate activity.
Subject to that prerequisite, component assessments exercise every claimed render mode; package-only does not use this
component runtime or lifecycle procedure. Create disposable consumer apps outside the reviewed
repository under the approved scratch/output root. Use the exact package, not a local rebuild.
Record restore/build/start commands, runtime observations, browser console/network evidence,
prerequisites and blockers. A build is not runtime proof. Stop at the approved timebox and keep
remaining applicable rows `not tested`.

Pin and record the supported SDK before restore. For `net10.0`, do not inherit a preview SDK
merely because it is the host default. A public NuGet v2 endpoint is an allowed read-only fallback
when v3 is unavailable; retain both outcomes and never edit reviewed manifests or configuration.
Remove disposable apps and raw logs after retaining bounded commands, results, relevant snippets
and digests. Retain a failed probe only when needed for reproduction and record why; exclude
secrets, credentials, unrelated private URLs and machine-specific absolute paths from reports.

## Render-mode matrix

Exercise every confirmed claimed mode using the exact package:

- `static-ssr`: prerender and useful noninteractive semantics;
- `interactive-server`: state changes, callbacks, reconnect/disposal boundaries;
- `interactive-webassembly` or `standalone-webassembly`: browser runtime and asset loading;
- `interactive-auto`: behavior before and after interactivity.

A build is not runtime proof. Confirm rendering/interactivity with public behavior plus browser
console/network evidence where applicable.

## Cheap preflight

Before broad or expensive probes: verify prerequisites and documented tool versions; confirm the
route/assets; build/start the smallest consumer; assert one target renders; run one critical
interaction; expand only after that smoke gate. Missing prerequisites, stale routes, absent assets,
or host startup failures are probe blockers unless direct evidence ties them to the package.

Retain the fixture's supported-context basis before scoring: applicable hosting mode, container
or form restrictions, registration, and the exact API/version used. Distinguish a documented
supported setup from an intentional unsupported-mode diagnostic or other negative control.
Current documentation may identify a caveat without proving the assessed release's contract.
If a contrast depends on a prohibited or unestablished setup, preserve it as a qualified
observation and repeat the smallest relevant supported-context case before attributing a defect.
Do not erase a valid unsupported-mode diagnostic when that diagnostic is itself the requirement.

Establish useful capture, not merely a successful automation command. At initial use and after
navigation or visibility changes, retain the actual target, renderer, delivered input event, and
corresponding state/callback or attributable failure. Accepted debugger/automation commands with
no delivered event do not demonstrate failed component input handling. Record target coverage
and page visibility, and compare a known input target when needed to distinguish delivery from
component behavior. Programmatic API calls or DOM changes may supply separately labeled evidence;
they do not replace a missing native interaction or prove visible UI.

## Inspect and exercise

- public parameters, mutation, binding pairs, required parameters, docs, and compatibility;
- callback awaiting, exception routing, renderer affinity, and rerendering;
- timers, subscriptions, cancellation, object/module/listener ownership, and async disposal;
- JS initialization, module scope, serialization, DOM sinks, and custom-element upgrade;
- CSS isolation or documented global styles;
- initial/update render, late children, keyed reorder, selected removal/disablement, navigation,
  reset, detach/reattach, repeated initialization, callback failure, and cancellation;
- typed values in both directions for every claimed supported shape.

Trace source through the public wrapper, inherited base/runtime types, and browser module handlers.
Direct source that synchronously inspects or discards an asynchronous callback/cleanup task can
establish a `gap` even when a separate runtime probe is blocked. A blocked runtime probe still
remains relevant for behavior that source cannot establish.

When a dynamic-child lifecycle row also has direct source proof, retain and cite the lifecycle
protocol. Use a canonical `source-proof-v1` protocol bound to the exact confirmed component source
artifact; free-text analysis is insufficient. For `BEQ-12`, use proof kind
`async-callback-not-awaited`. For `BEQ-15`, use `async-cleanup-not-awaited`. A source-proven gap is
valid only when the mapped lifecycle operation is failed or not tested. A passed mapped operation
contradicts the source proof and must be reconciled instead of bypassed.

When dynamic-child lifecycle is explicitly `not-applicable`, a matching `source-proof-v1`
may establish a `BEQ-12` callback or `BEQ-15` cleanup source `gap` without a lifecycle companion.
The confirmed declaration still requires its rationale and no triggers; missing, unknown or
malformed applicability is not permission to use this path. Do not invent lifecycle operations,
change the applicability declaration, or leave an established source conflict unscored merely
because the component has no dynamic children.

Use the same requirement-specific proof kinds and exact confirmed source path/digest, bound by
the selected component's outer evidence and final input identity. Register the actual proof,
confirm final inputs, and use the existing identity, evidence and assessment producers. This
remains typed, gap-only evidence, not a free-text workaround or proof that a runtime operation
ran. For lifecycle-required components, the companion, mapped outcomes and contradiction rules
above remain mandatory and unchanged.
The [synthetic worked example](#synthetic-source-finding-example) below materializes both protocols
and follows the existing input/identity/evidence producers without executing the source.

For documentation requirements, direct absence from the confirmed complete public corpus is a
`gap`. For genuinely disjunctive requirements, do not call a gap until every remaining applicable
alternative is directly contradicted. Conditional render-mode rows are `not applicable` when that
mode is not a confirmed support claim.

For current `2.1.0` `BEQ-03`, require supported-mode documentation **and** a clear compile-time
**or** runtime error for an applicable unsupported-mode diagnostic. Documentation alone does not
discharge the error obligation; an error alone does not discharge documentation. A directly
established missing required conjunct is a `gap`. A blocked or unperformed applicable diagnostic
with no direct conflict stays `not tested`, not an inferred pass or absence gap.

Clause 4.2 retains the alternative of all modes working correctly versus a documented supported set
with clear errors elsewhere. Do not invent an unsupported configuration or require support for an
unsupported mode. Valid prerendering for supported interactive modes is not an unsupported-mode
diagnostic. Keep `BEQ-02` and `BEQ-04` independent; prerendering must not throw.

For `BEQ-05`, verify correct static-SSR behavior only when it is a confirmed support
claim. A temporary placeholder during interactive prerendering is not automatically
a static-SSR defect, and static SSR does not mean JavaScript must be disabled.
There is no extra requirement to publish an SSR usefulness/accessibility contract.
Unknown applicability or an unperformed observation is not a verified or N/A result.

A `verified` or `gap` outcome uses `protocol:static-ssr-behavior-v1`, bound through
the normal evidence producer to the exact confirmed component/package identity.
Retain the actual observation as an `evidence_inputs` entry of kind `raw-observation`:

```json
{"schema_version":1,"observation":"static-ssr-behavior","component_id":"<confirmed-id>","mode":"static-ssr","observed_identity":"static","expected_behavior":"<behavior being checked in the supported context>","observed_behavior":"<actual captured result and limitations>","result":"passed"}
```

Use `failed` only for an observed contradiction, not an unavailable probe. Bind that
capture's actual SHA-256 in the canonical protocol:

```json
{"schema_version":1,"protocol":"static-ssr-behavior","component_id":"<confirmed-id>","result":"passed","raw_observation_sha256":{"algorithm":"sha256","value":"<actual-raw-observation-sha256>"}}
```

Each protocol result must agree with its own raw observation. Multiple current
observations may be cited: `verified` requires a passed observation and no cited
failed observation; mixed passed/failed observations may support `gap`.
Unselected historical records do not determine the row's status. Preserve the
existing supersession rules rather than discarding history or selecting both
a record and its superseded ancestor.

Register the actual captured files and confirm the final
manifest before initializing/exporting identity. These examples describe record
shape, not executed evidence; do not fabricate renderer identity or observations.
The validator checks correspondence, not execution authenticity, semantic truth
or complete accessibility conformance. Documentation alone is not runtime proof.

Implementation-mechanism rows require mechanism evidence. A successful behavior probe does not
prove `@key`, awaited callbacks, renderer affinity, `StateHasChanged`, or async disposal. Complete
source may establish that no compile-time-required parameter exists for an `[EditorRequired]`
surface; otherwise leave applicability unresolved as `not tested`.

For `BEQ-09`, inspect every component-owned assignment to each public `[Parameter]` property,
including assignments in event handlers, callbacks, conditional branches, and inherited members.
Any such assignment is parameter mutation and establishes a `gap`. Using a backing field on other
paths, invoking the paired callback, or avoiding the setter during ordinary rendering does not
cancel a direct assignment through the public parameter setter.

For navigation over disabled/hidden items, include all-disabled/no-focusable cases and assert
termination. Report a shared-runtime problem at its owning layer while retaining the assigned
component as demonstrated scope. Never generalize one component's result to siblings.

## Conditional dynamic-child lifecycle matrix

Apply this matrix only when the confirmed component groups, registers, selects, or composes child
items. The input manifest must declare `dynamic_child_lifecycle.applicability` as `required` with
one or more matching triggers. A component with no such surface declares `not-applicable` with a
specific rationale and is not forced through these probes.

After initial render and upgrade, obtain a raw observation for every operation:

1. add one child;
2. remove one child;
3. reorder keyed children;
4. disable or remove the selected child;
5. reconcile membership;
6. propagate name, default, value, and selected state;
7. transfer and restore focus ownership;
8. preserve exactly one expected roving tab stop;
9. route callbacks and callback failures;
10. clean up registrations, listeners, references, and async disposal.

The lifecycle protocol must contain all ten operations in canonical order. Each operation is either
`observed`, with a `passed` or `failed` outcome and a digest-bound raw observation, or `not-tested`,
with an operation-specific blocker. Initial-state-only evidence, a prose checklist, or one aggregate
browser summary cannot verify the matrix.

Confirm the protocol and raw files under `evidence_inputs`. Register the protocol evidence as
`reproduced-runtime-observation` with the protocol basename as locator and
`protocol:dynamic-child-lifecycle-v1` as `provenance.method`.

For a claimed Interactive Auto mode, a verified BEQ-08 requires the named
`protocol:auto-renderer-transition-v1` protocol. Its cold visit must observe the Server renderer
and its warm full-document revisit must observe the WebAssembly renderer as distinct execution
identities. Identical DOM, downloaded WebAssembly assets, or an unchanged `interactive-auto`
label are not renderer identity evidence; if the identities cannot be observed, use `not tested`
or `gap` according to the status boundaries.

## Synthetic source-finding example

This is one static, deliberately synthetic `BEQ-12` gap, not a vendor assessment or a new
assessment route. In [SyntheticCallbackGroup.cs.txt](../assets/source-finding/SyntheticCallbackGroup.cs.txt),
`SelectAsync` discards the task from `SelectionChanged.InvokeAsync(child)` and returns
`Task.CompletedTask`. That source establishes the unawaited-callback mechanism, not an observed
browser failure. Returning or awaiting the callback task is the bounded correction; renderer
affinity, error routing and all other behavior still require their own evidence.

### Prerequisites and ownership

Continue from the [existing typed intake and confirmation flow](input-candidates.md).
The reader must already own a **synthetic** package/source setup corresponding to this exact
source. The example does not supply a package, acquire source, create a package, or establish
package/source correspondence by renaming files. If that setup is unavailable, stop here.

| Input or output | Availability and meaning |
|---|---|
| `$SkillDir` | Directory containing the loaded `SKILL.md`, with its trusted validator and plugin-local inert template; no checkout/cache fallback. |
| `$InputRoot` | Existing, user-approved external retained-input directory, outside the plugin and reviewed checkout. Contains `package.nupkg` and every supporting file referenced by the retained candidates. |
| `SyntheticCallbackGroup.cs` | Exact template bytes retained under `$InputRoot` during prerequisite intake, read/copied/hashed as data only. Never compile, script, `Add-Type`, reflection-load or run either representation. |
| `$Candidates`, `$Confirmed` | Latest producer-created intake candidates and their prior confirmed manifest, both beneath `$InputRoot`. Preserve them; do not turn a manifest back into candidates or substitute a hidden golden bundle. |
| `$ComponentId`, `$SourcePath` | Exact canonical component ID and logical repository-relative source path from that confirmed state. Its source artifact must point to `SyntheticCallbackGroup.cs`, and its allowlist must contain `$SourcePath`. |
| Source/lifecycle context | Confirmed `source-available` package mapping, exact commit and confidence, with truthful synthetic correspondence. The component must have `required` lifecycle applicability and supported grouping/registration/selection/composition triggers. |
| `$BuildScratch` | Fresh external writable validator scratch, outside both the plugin and retained inputs, with an existing parent. Active .NET 11 SDK and **PowerShell Core 7.3 or later within 7.x** are required for the explicit generic-method syntax and `JsonNode` APIs; no installation/fallback here. |
| Example outputs | Two new root-level protocol files, new candidates/confirmed manifest, identity, drafts, ledger, bundle and incomplete assessment. Nothing pre-scored is an input. No report, revision or reader is produced by this bounded example. |

Contract tests supply equivalent prerequisites using existing **test-only** synthetic package
fixtures and typed intake producers. Those fixture outputs are not reader deliverables. Their
local-preview plugin-directory copy checks content availability, not archive, installation,
distribution or NuGet packaging.

### Retain the inert template during prerequisite intake

If a fresh retained copy is needed, save and run this block before the existing intake producers.
Do not overwrite an already registered file. The template must be a regular file delivered with
this skill; its `.txt` suffix alone is not proof of exclusion from compilation.
Hidden files and directories use the same prerequisites: `Get-Item -Force` makes them visible
without bypassing the regular-file, link, containment or no-overwrite checks.

```powershell
param([string]$SkillDir, [string]$InputRoot)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Core' -or
    $PSVersionTable.PSVersion.Major -ne 7 -or $PSVersionTable.PSVersion.Minor -lt 3) {
    throw 'This example requires PowerShell Core 7.3 or later within 7.x.'
}
$SkillDir = (Get-Item -Force -LiteralPath $SkillDir).FullName
$InputRoot = (Get-Item -Force -LiteralPath $InputRoot).FullName
$Template = Join-Path $SkillDir 'assets/source-finding/SyntheticCallbackGroup.cs.txt'
if (-not (Test-Path -LiteralPath $Template -PathType Leaf)) {
    throw 'Missing source-finding template in the loaded skill; no checkout fallback.'
}
$item = Get-Item -Force -LiteralPath $Template
if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw 'The source-finding template must be a regular file, not a link.'
}
$plugin = [IO.Path]::GetFullPath((Join-Path $SkillDir '../..'))
$parentPrefix = '..' + [IO.Path]::DirectorySeparatorChar
$relative = [IO.Path]::GetRelativePath($plugin, $InputRoot)
if ($relative -ne '..' -and -not $relative.StartsWith($parentPrefix, [StringComparison]::Ordinal) -and
    -not [IO.Path]::IsPathRooted($relative)) {
    throw 'Retained source must be outside the plugin.'
}
$RetainedSource = Join-Path $InputRoot 'SyntheticCallbackGroup.cs'
[IO.File]::Copy($Template, $RetainedSource, $false)
if ([Convert]::ToHexString([IO.File]::ReadAllBytes($Template)) -cne
    [Convert]::ToHexString([IO.File]::ReadAllBytes($RetainedSource))) {
    throw 'Retained source differs from the delivered template.'
}
```

Register that retained file using `inputs candidates add-source-artifact` with the actual
`--source-path` and `--path SyntheticCallbackGroup.cs`, and register the component through
`add-component` in the existing intake flow. Retain the latest candidates after discovery and
explicit confirmation. Do not relabel a real package or assert a repository mapping you do not own.

### Validate the existing setup

Save the following four PowerShell blocks, in order, as one trusted example-authoring `.ps1`
outside retained inputs. Run it with an explicitly resolved `pwsh.exe` on Windows (or `pwsh`
elsewhere), `-NoLogo -NoProfile -NonInteractive -File`, and the named prerequisite parameters.
`-ConfirmNewInputs` represents the operator's explicit decision to reconfirm the two newly
authored artifacts; it is not authorization to execute assessed code. No runtime operation is
performed. Do not reuse output names after a partial attempt.

```powershell
param(
    [string]$SkillDir, [string]$InputRoot, [string]$Candidates, [string]$Confirmed,
    [string]$ComponentId, [string]$SourcePath, [string]$BuildScratch,
    [switch]$ConfirmNewInputs
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Core' -or
    $PSVersionTable.PSVersion.Major -ne 7 -or $PSVersionTable.PSVersion.Minor -lt 3) {
    throw 'This example requires PowerShell Core 7.3 or later within 7.x.'
}
if (-not $ConfirmNewInputs) { throw 'Explicit reconfirmation of the new inputs is required.' }
foreach ($path in @($SkillDir, $InputRoot, $Candidates, $Confirmed, $BuildScratch)) {
    if (-not [IO.Path]::IsPathFullyQualified($path)) { throw 'Use absolute prerequisite paths.' }
}
$SkillDir = (Get-Item -Force -LiteralPath $SkillDir).FullName
$InputRoot = (Get-Item -Force -LiteralPath $InputRoot).FullName
$plugin = [IO.Path]::GetFullPath((Join-Path $SkillDir '../..'))
$parentPrefix = '..' + [IO.Path]::DirectorySeparatorChar
foreach ($pair in @(@($plugin, $InputRoot), @($plugin, $BuildScratch), @($InputRoot, $BuildScratch))) {
    $relative = [IO.Path]::GetRelativePath($pair[0], $pair[1])
    if ($relative -ne '..' -and -not $relative.StartsWith($parentPrefix, [StringComparison]::Ordinal) -and
        -not [IO.Path]::IsPathRooted($relative)) {
        throw 'Retained inputs and build scratch must respect the external-directory boundary.'
    }
}
foreach ($path in @($Candidates, $Confirmed)) {
    $relative = [IO.Path]::GetRelativePath($InputRoot, $path)
    if ($relative -eq '..' -or $relative.StartsWith($parentPrefix, [StringComparison]::Ordinal) -or
        [IO.Path]::IsPathRooted($relative)) {
        throw 'Retained candidates and confirmed manifest must be beneath InputRoot.'
    }
}
$Template = Join-Path $SkillDir 'assets/source-finding/SyntheticCallbackGroup.cs.txt'
$RetainedSource = Join-Path $InputRoot 'SyntheticCallbackGroup.cs'
$Package = Join-Path $InputRoot 'package.nupkg'
$Launcher = Join-Path $SkillDir 'scripts/validator/run-validator.ps1'
foreach ($path in @($Template, $RetainedSource, $Package, $Candidates, $Confirmed, $Launcher)) {
    $item = Get-Item -Force -LiteralPath $path
    if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'Example prerequisites must be existing regular files.'
    }
}
if (Test-Path -LiteralPath $BuildScratch) { throw 'Use fresh validator scratch.' }
if (-not (Test-Path -LiteralPath (Split-Path -Parent $BuildScratch) -PathType Container)) {
    throw 'The build scratch parent must already exist.'
}
$ExampleDir = Join-Path $InputRoot 'source-finding-output'
$ProofPath = Join-Path $InputRoot 'callback-source-proof.json'
$LifecyclePath = Join-Path $InputRoot 'callback-lifecycle.json'
foreach ($path in @($ExampleDir, $ProofPath, $LifecyclePath)) {
    if (Test-Path -LiteralPath $path) { throw "Example output already exists: $path" }
}
$SourceDigest = (Get-FileHash -LiteralPath $RetainedSource -Algorithm SHA256).Hash.ToLowerInvariant()
if ([Convert]::ToHexString([IO.File]::ReadAllBytes($Template)) -cne
    [Convert]::ToHexString([IO.File]::ReadAllBytes($RetainedSource))) {
    throw 'The existing synthetic source must exactly match the delivered template.'
}
$prior = Get-Content -Raw -LiteralPath $Confirmed | ConvertFrom-Json
$facts = Get-Content -Raw -LiteralPath $Candidates | ConvertFrom-Json
$component = @($prior.components | Where-Object { $_.id -ceq $ComponentId })
$source = @($prior.source_artifacts | Where-Object { $_.source_path -ceq $SourcePath })
$candidateSource = @($facts.source_artifacts | Where-Object { $_.source_path -ceq $SourcePath })
$candidateComponent = @($facts.components | Where-Object { $_.id -ceq $ComponentId })
if ($prior.state -cne 'confirmed' -or $prior.source.availability -cne 'source-available' -or
    $prior.package.nupkg_path -cne 'package.nupkg' -or $component.Count -ne 1 -or
    $source.Count -ne 1 -or $candidateSource.Count -ne 1 -or $candidateComponent.Count -ne 1 -or
    $source[0].content_path -cne 'SyntheticCallbackGroup.cs' -or
    $candidateSource[0].content_path -cne 'SyntheticCallbackGroup.cs' -or
    $source[0].content_sha256.value -cne $SourceDigest -or
    $component[0].allowed_source_paths -cnotcontains $SourcePath -or
    $candidateComponent[0].allowed_source_paths -cnotcontains $SourcePath -or
    $component[0].dynamic_child_lifecycle.applicability -cne 'required' -or
    $candidateComponent[0].dynamic_child_lifecycle.applicability -cne 'required') {
    throw 'Incompatible synthetic source/component/lifecycle prerequisite state.'
}
$env:READINESS_TEMP = $BuildScratch
function Readiness {
    & $Launcher @args
    if ($LASTEXITCODE -ne 0) { throw "Readiness failed ($LASTEXITCODE): $($args -join ' ')" }
}
Readiness inputs validate --root $InputRoot --manifest $Confirmed
$null = New-Item -ItemType Directory -Path $ExampleDir
```

### Materialize both complete protocols

These are the complete ordered shapes. The BCL `System.Text.Json` writer below emits compact
default-encoder UTF-8 **without BOM or trailing newline**, preserving the displayed property
and operation order. Do not save the pretty shapes unchanged. Only the logical source path
and exact retained-byte SHA-256 are substituted; no `component_id` field belongs in either protocol.
The ten operation-specific reasons truthfully describe this static-only example's unperformed
work. Protocol `not-tested` is distinct from the assessment row status `not tested`.

```powershell
function WriteExampleJson([string]$Path, [System.Text.Json.Nodes.JsonNode]$Value) {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
    $writer = [System.Text.Json.Utf8JsonWriter]::new($stream)
    try { $Value.WriteTo($writer); $writer.Flush() }
    finally { $writer.Dispose(); $stream.Dispose() }
}
$proof = [System.Text.Json.Nodes.JsonNode]::Parse(@'
{
  "schema_version": 1,
  "protocol": "source-proof",
  "requirement_id": "BEQ-12",
  "proof_kind": "async-callback-not-awaited",
  "result": "failed",
  "source_path": "REPLACED_FROM_CONFIRMED_SOURCE",
  "source_sha256": { "algorithm": "sha256", "value": "REPLACED_FROM_RETAINED_BYTES" }
}
'@)
$proof['source_path'] = [System.Text.Json.Nodes.JsonValue]::Create([string]$SourcePath)
$proof['source_sha256']['value'] = [System.Text.Json.Nodes.JsonValue]::Create([string]$SourceDigest)
$lifecycle = [System.Text.Json.Nodes.JsonNode]::Parse(@'
{
  "schema_version": 1,
  "protocol": "dynamic-child-lifecycle",
  "operations": [
    { "operation": "add-child", "disposition": "not-tested", "outcome": null, "raw_observation_sha256": null, "not_tested_reason": "Static-only example: no authorized runtime host was started to add a child." },
    { "operation": "remove-child", "disposition": "not-tested", "outcome": null, "raw_observation_sha256": null, "not_tested_reason": "Static-only example: no authorized runtime host was started to remove a child." },
    { "operation": "keyed-reorder", "disposition": "not-tested", "outcome": null, "raw_observation_sha256": null, "not_tested_reason": "Static-only example: keyed children were not rendered or reordered." },
    { "operation": "disable-or-remove-selected-child", "disposition": "not-tested", "outcome": null, "raw_observation_sha256": null, "not_tested_reason": "Static-only example: no selected child was disabled or removed in a runtime host." },
    { "operation": "membership-reconciliation", "disposition": "not-tested", "outcome": null, "raw_observation_sha256": null, "not_tested_reason": "Static-only example: rendered membership reconciliation was not exercised." },
    { "operation": "propagated-name-default-value-state", "disposition": "not-tested", "outcome": null, "raw_observation_sha256": null, "not_tested_reason": "Static-only example: propagation of name, default, value and selected state was not exercised." },
    { "operation": "focus-ownership-restoration", "disposition": "not-tested", "outcome": null, "raw_observation_sha256": null, "not_tested_reason": "Static-only example: no browser was started to observe focus ownership or restoration." },
    { "operation": "single-roving-tab-stop", "disposition": "not-tested", "outcome": null, "raw_observation_sha256": null, "not_tested_reason": "Static-only example: browser tab stops were not observed or counted." },
    { "operation": "callbacks-error-routing", "disposition": "not-tested", "outcome": null, "raw_observation_sha256": null, "not_tested_reason": "Static-only example: callback completion and error routing were not executed; source inspection is separate evidence." },
    { "operation": "cleanup-disposal", "disposition": "not-tested", "outcome": null, "raw_observation_sha256": null, "not_tested_reason": "Static-only example: disposal, subscriptions and registration cleanup were not executed." }
  ]
}
'@)
WriteExampleJson $ProofPath $proof
WriteExampleJson $LifecyclePath $lifecycle
```

### Reconfirm inputs and bind the evidence

This is the [normal producer sequence](input-candidates.md), specialized only to these two
artifacts. Append to the retained candidates, never a reconstructed subset. Both belong in
`evidence_inputs`, not `owner_inputs`. Component association comes from the confirmed allowlist,
exact exported assessment identity and outer component-specific records. The existing lifecycle
kind is `reproduced-runtime-observation` even when all operations are blocked; that kind does
**not** mean an operation ran. The claim explicitly records that none did.

```powershell
$next = Join-Path $ExampleDir 'candidates-proof.json'
$finalCandidates = Join-Path $ExampleDir 'candidates-both.json'
$draftInput = Join-Path $ExampleDir 'inputs-draft.json'
$finalInput = Join-Path $ExampleDir 'inputs-confirmed.json'
Readiness inputs candidates add-evidence --input $Candidates --path callback-source-proof.json --kind structured-protocol --output $next
Readiness inputs candidates add-evidence --input $next --path callback-lifecycle.json --kind structured-protocol --output $finalCandidates
Readiness inputs discover --root $InputRoot --nupkg $Package --candidates $finalCandidates --output $draftInput
Readiness inputs confirm --root $InputRoot --draft $draftInput --output $finalInput
Readiness inputs validate --root $InputRoot --manifest $finalInput
$initialized = Join-Path $ExampleDir 'assessment-initial.json'
$identity = Join-Path $ExampleDir 'assessment-identity.json'
Readiness assessment init --kind component --component $ComponentId --root $InputRoot --input $finalInput --output $initialized
Readiness assessment export-identity --assessment $initialized --output $identity
$proofDraft = Join-Path $ExampleDir 'proof-draft.json'
$bothDraft = Join-Path $ExampleDir 'evidence-draft.json'
$CapturedAt = [DateTime]::UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", [System.Globalization.CultureInfo]::InvariantCulture)
Readiness evidence draft-add --output $proofDraft --claim 'Static inspection of SyntheticCallbackGroup.SelectAsync found the discarded SelectionChanged.InvokeAsync task followed by Task.CompletedTask; no runtime execution.' --scope component-specific --component $ComponentId --kind reviewer-generated-analysis --root $InputRoot --manifest $finalInput --evidence-input callback-source-proof.json --method protocol:source-proof-v1 --captured-at $CapturedAt
Readiness evidence draft-add --input $proofDraft --output $bothDraft --claim 'No runtime operation was performed. The lifecycle companion records ten operation-specific not-tested reasons for this static-only example.' --scope component-specific --component $ComponentId --kind reproduced-runtime-observation --locator callback-lifecycle.json --content $LifecyclePath --method protocol:dynamic-child-lifecycle-v1 --captured-at $CapturedAt
$ledgerPath = Join-Path $ExampleDir 'component-ledger.json'
Readiness evidence ledger-build --kind component --subject $identity --draft $bothDraft --nupkg $Package --output $ledgerPath
Readiness evidence ledger-validate --ledger $ledgerPath
$ledger = Get-Content -Raw -LiteralPath $ledgerPath | ConvertFrom-Json
$proofRecords = @($ledger.records | Where-Object { $_.provenance.method -ceq 'protocol:source-proof-v1' -and $_.provenance.locator -ceq 'callback-source-proof.json' })
$lifecycleRecords = @($ledger.records | Where-Object { $_.provenance.method -ceq 'protocol:dynamic-child-lifecycle-v1' -and $_.provenance.locator -ceq 'callback-lifecycle.json' })
if ($proofRecords.Count -ne 1 -or $lifecycleRecords.Count -ne 1) { throw 'Expected one record for each authored protocol.' }
$Ids = @($proofRecords[0].stable_id, $lifecycleRecords[0].stable_id)
$bundle = Join-Path $ExampleDir 'evidence.json'
Readiness evidence bundle --assessment $identity --source-ledger $ledgerPath --ids ($Ids -join ',') --root $InputRoot --manifest $finalInput --output $bundle
```

`$CapturedAt` is the actual authoring/inspection time for these new records, not a package
publication timestamp. The exported identity is used unchanged. Ledger validation alone is
structural; the explicit root/manifest bundle accepts input linkage, not factual truth.

### Author only the source-backed gap

```powershell
$assessment = [System.Text.Json.Nodes.JsonNode]::Parse([IO.File]::ReadAllText($initialized))
$rows = @($assessment['rows'].AsArray() | Where-Object { $_['id'].GetValue[string]() -ceq 'BEQ-12' })
if ($rows.Count -ne 1) { throw 'Expected exactly one BEQ-12 row.' }
$row = $rows[0]
$row['status'] = [System.Text.Json.Nodes.JsonValue]::Create([string]'gap')
$row['observation'] = [System.Text.Json.Nodes.JsonValue]::Create([string]'SyntheticCallbackGroup.SelectAsync discards SelectionChanged.InvokeAsync(child) and returns Task.CompletedTask. This establishes an unawaited asynchronous callback in the confirmed source; runtime behavior and error routing were not tested.')
$row['owner_action'] = [System.Text.Json.Nodes.JsonValue]::Create([string]'Return or await the callback task so completion and failures can propagate; validate runtime routing separately in an authorized host.')
foreach ($id in $Ids) { $row['evidence_ids'].AsArray().Add([System.Text.Json.Nodes.JsonValue]::Create([string]$id)) }
$rowDraft = Join-Path $ExampleDir 'assessment-draft.json'
$canonical = Join-Path $ExampleDir 'assessment.json'
WriteExampleJson $rowDraft $assessment
Readiness assessment canonicalize --assessment $rowDraft --output $canonical
Readiness assessment validate --root $InputRoot --input $finalInput --assessment $canonical --evidence $bundle
```

The native validator must accept the result **and** inspection must show both evidence IDs on
`BEQ-12`, all unrelated rows still null with empty dependent fields, and `completion_state:
incomplete`. Do not fill other rows with `not applicable` or mark the assessment complete.
This leaves runtime, accessibility, trim/AOT and all other unperformed work unverified.
Continue the ordinary assessment/report workflow only when separately requested; this example
does not replace it. Preserve prior input bytes and every new partial artifact on failure.

## Interactive Auto evidence protocol

Retain one canonical `structured-protocol` input for the protocol and one canonical
`raw-observation` input for each visit. The protocol shape is:

```json
{
  "schema_version": 1,
  "protocol": "auto-renderer-transition",
  "component_id": "Example.Calendar",
  "cold": {
    "visit": "cold",
    "expected_identity": "server",
    "observed_identity": "server",
    "raw_observation_sha256": {
      "algorithm": "sha256",
      "value": "<cold-capture-sha256>"
    }
  },
  "warm": {
    "visit": "warm",
    "expected_identity": "webassembly",
    "observed_identity": "webassembly",
    "raw_observation_sha256": {
      "algorithm": "sha256",
      "value": "<warm-capture-sha256>"
    }
  }
}
```

Each referenced raw visit is canonical JSON with this shape:

```json
{
  "schema_version": 1,
  "observation": "auto-renderer-visit",
  "component_id": "Example.Calendar",
  "visit": "cold",
  "mode": "interactive-auto",
  "observed_identity": "server"
}
```

Use `visit: "warm"` and `observed_identity: "webassembly"` for the second capture. The
`component_id`, visit, mode, and observed identity must agree across the protocol and its raw
capture. The protocol digest must equal the `structured-protocol` input digest, each raw digest
must equal its `raw-observation` input digest, and the selected evidence record must use
`protocol:auto-renderer-transition-v1` with the protocol basename as locator. Populate
`observed_identity` from the retained browser/console/network capture of that visit; never copy
`expected_identity` into the observed field. The validator binds these digests and identities but
does not turn a declaration into independent real-world proof.
