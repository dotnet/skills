# Synthetic source-finding example

First read the [runtime rules](area-blazor-runtime.md); this example does not replace them.

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
