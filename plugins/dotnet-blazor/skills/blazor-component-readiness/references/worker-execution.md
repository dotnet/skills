# Worker execution

## Capability boundary

A readiness worker owns validator artifacts, so it must run as the selected top-level agent in a
separate writable session. Nested custom agents are text-only review/delegation contexts and cannot
serve as readiness workers. Do not stream assessment files through nested-agent response text or
let the coordinator reconstruct worker-owned evidence.

If the host cannot create a separate writable session, stop with:

`unsupported host: readiness worker requires a top-level writable session`

For full-library work, use the library-level unsupported-host message required by `SKILL.md`.

## Per-unit directory

Create one directory per confirmed unit below the approved run root. Before launch:

An explicitly assigned scoped unit must first read [the profile](scoped-component-profile.md):
stage its exact scoped-context revision/closure and use `--package-context-revision`, with
`--package-context-feedback` when bound, instead of the ordinary full-package binding below.
The 48 context checks are not component findings or ordinary library completion.

1. Copy only the exact confirmed input manifest, package bytes, validated package revision, the
   package-wide files referenced by that revision's input manifest, unit-specific
   source/documents/evidence, worker prompt, and a trusted plugin copy.
2. Keep sibling component files and the reviewed repository outside the worker directory.
3. Record the staged file inventory and SHA-256 values.
4. Give the worker permission to write only inside its working directory. Do not enable unrestricted
   path access or remote mutation.

The worker writes its revision beneath this directory and returns only the structured handoff.
Package-wide binding files are required so the worker can revalidate the package revision; they do
not authorize sibling component source, documentation, candidates, or evidence. Package inventory
entries inside the exact binding remain non-evidence for component work and must not be cited or
reproduced in component artifacts.

Carry the absolute trusted plugin root in the coordinator-to-worker launch context. The trusted
root is the staged plugin inside the unit, not a host cache or sibling checkout; it is execution
context, not an assessment-schema field. When this explicit root is supplied, the exact bundled
`<trusted-plugin-root>/skills/blazor-component-readiness/SKILL.md` is authoritative: load that
complete file first, do not invoke an unqualified registered skill by name, and do not accept an
inherited or global same-name skill. Resolve every referenced file, including the validator,
relative to that loaded file. Record the activation method and exact source path. If loading the
exact bundled file is denied by permissions or content exclusion, fail closed and never try
another path. In contexts without an explicit trusted root, normal registered-skill behavior may
be used; it must not override an explicit-root launch.

Before launching the worker, use the shipped
`skills/blazor-component-readiness/scripts/prepare-worker-launch.sh` helper from the installed
plugin. Give it the unit directory, the staged plugin path, a base prompt without any plugin-root
line, and an output path below the unit. Source the generated contract and use both
`--plugin-dir "$CLI_PLUGIN_DIR"` and the generated `WORKER_PROMPT_FILE`; the helper validates the
same absolute in-unit root for both values. It rejects missing or out-of-unit plugins and rejects
prompts that independently provide a relative or mismatched plugin root. Do not hand-compose either
path after the helper succeeds. The output directory must already exist; keep the base prompt,
generated `worker-prompt.txt`, and shell contract at distinct paths. This helper uses Bash.

```text
set -euo pipefail
UNIT="<absolute-unit-directory>"
PLUGIN="$UNIT/tooling/plugins/<plugin-name>"
CONTRACT="$UNIT/launch.env"
"$PLUGIN/skills/blazor-component-readiness/scripts/prepare-worker-launch.sh" \
  --unit "$UNIT" --plugin-dir "$PLUGIN" --prompt-file "$UNIT/worker-prompt-base.txt" \
  --output "$CONTRACT"
. "$CONTRACT"
mkdir -p "$UNIT/logs" "$UNIT/tmp"
cd "$UNIT"
export PWD="$UNIT" TMPDIR="$UNIT/tmp"
copilot --agent <plugin-name>:blazor-component-readiness-worker \
  --plugin-dir "$CLI_PLUGIN_DIR" \
  -C "$UNIT" \
  --model <owner-approved-model> \
  --effort <owner-approved-effort> \
  --session-id <new-session-uuid> \
  --log-dir "$UNIT/logs" \
  --allow-all-tools \
  --disable-builtin-mcps \
  --no-ask-user \
  --no-auto-update \
  --silent \
  -p "$(cat "$WORKER_PROMPT_FILE")"
```

The coordinator agent must invoke this complete recipe, rather than separately constructing a
plugin path or worker prompt. Select the qualified worker as a new top-level process. Use the
installed plugin name from `plugin.json`; do not guess an unqualified agent name. The generated
prompt is the explicit source-selection handoff and must be passed unchanged.

Use the unit directory as the validator input root. Keep confirmed package/component inputs and the
validated package revision directly beneath that root, and place new component artifacts beneath
the assigned output subdirectory. Do not use the output subdirectory as `--root` or relocate bound
inputs into it.

`--allow-all-tools` is required for non-interactive execution; it does not authorize paths outside
the worker working directory. Never add `--allow-all-paths`. Permit network URLs only when the
confirmed unit requires public retrieval and the owner approved those exact domains.

Resolve the plugin directory and unit directory to absolute paths before invoking the CLI. Change
directory to the unit before launching and set inherited `PWD` to that same absolute path; keep
temporary files, output, logs, and the staged plugin below the unit. Invoke `copilot` from `PATH`,
not a guessed SDK/cache executable. Record the exact native session UUID, full argv, actual cwd and
PWD, native exit code, stderr, tool-call IDs/results, and any raw permission or content-policy
failure. A prose summary is not a host error. Some hosts apply `-C` before resolving other
relative arguments, which can make a valid staged plugin undiscoverable. Launch once per unit
attempt. Treat usage output, a nonzero exit, timeout, or missing handoff as a failed attempt; do
not retry in the same directory. A coordinator may reconcile the failure by creating a fresh
attempt in a new unit directory.

`--available-tools` may restrict a startup check to the declared skill/read tools; do not broaden
filesystem or URL permissions to compensate for a lookup failure.

Use the host's equivalent top-level-session API when available. In an SDK host, create a new
session with the unit directory as `WorkingDirectory`, register/select only the worker persona and
required skill, and apply the same path and network restrictions. In Agency, use one job/session
per unit. VS, VS Code, or cloud hosts without a sibling writable-session primitive must fail closed.

## Acceptance

Treat worker output as untrusted until the coordinator:

1. Confirms exactly one process/session was launched, returned the exact assigned unit ID, and
   returned no blockers with success-shaped artifact paths.
2. Resolves every returned path below the assigned unit output root.
3. Recomputes the validation-manifest SHA-256 from final bytes.
4. Runs deterministic report verification with the exact confirmed input and package revision.
5. Confirms row ownership, package binding, claimed-mode dispositions, and absence of sibling
   evidence.

Normalize accepted handoff paths relative to the confirmed output root, without `./` or the
output-root directory name as a prefix. This storage convention is not the verification argv:
resolve returned paths to absolute paths using the worker's recorded validator input root before
calling the validator. Relative CLI file arguments resolve beneath `--root`, not beneath the
coordinator's current directory. For example, with `/work/unit/output` as the root, use
`--revision /work/unit/output/revisions/0001` (or `--revision revisions/0001`), never
`--revision output/revisions/0001`. Apply the same rule to reader output and package bindings.
Pass absolute `--root`, `--revision`, `--output`, and `--package-revision` paths for ordinary
verification, or the profile's scoped-context flags instead. Never prepend the artifact root
to a path that already includes it.

Apply the [shared coverage gate](assessment-workflow.md#3-apply-statuses-and-reconcile-coverage)
before accepting any revision, including one produced in the coordinator context. Reconcile
each accessible evidence family and reject skipped claimed modes, duplicate ownership, sibling
leakage, repository-wide component records or wrong package binding. Low record count alone
is not failure when compact shared evidence supports every cited claim.

Write a completed handoff only after the requested investigation and final verification finish.
A structurally valid report whose accessible requested probes were never attempted is unfinished
work, not a completed unit. Genuine blocked probes and agreed scope limits remain truthful
`not tested` rows; a blanket lack of supplied runtime results is not a probe attempt.
Preserve immutable diagnostics and continue missing work within the timebox rather than closing
with empty blockers. A unit-level blocked, timed-out, duplicated, or digest-mismatched worker
remains incomplete and produces no success-shaped handoff.
