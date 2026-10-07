# MAUI doctor evaluation contracts

Failure classification: the supplied judge evidence identified a content defect
("its CI script contains a material manifest feature-band bug" and "CI discovery
code can select the wrong SDK manifest"). The old eval also encoded unsupported
vendor-failure assumptions and blanket command bans: that is coupled eval-design
bias, not independent proof of correctness. Common-path inventory and mandatory
reference discovery are scope/cost risks; SDK usage counters are not invoice
cost-neutrality evidence.

Follow-up transcripts also exposed routing/spec ambiguity: the exact-SDK CI
question did not establish a MAUI project, and neither skilled arm loaded Doctor.
The prompt now identifies the application framework without relaxing the skill's
non-MAUI boundary. The API override case explicitly identifies the compile TFM
and workload support, rather than conflating compile API with runtime SDK policy.
Manifest-only provisioning of an older platform is not a valid answer.

The suite covers SDK selection, missing pins, JDK path disagreement, uncertain
vendor claims, healthy no-op behavior, host/target limits, Apple version pairing,
evidenced workload corruption, project API overrides and manifest-band discovery.
The runtime-crash case is a non-voting dormancy contract. Eleven preference-eligible
cases exceed the five-case floor without treating repeats as new evidence; this
does not guarantee statistical power or a positive measured verdict.

Fixtures are honest offline inputs:

- `healthy/` contains illustrative **supplied evidence** and configuration for a
  read-only consistency review. Its minimal project is not a runnable application
  and is never built. No fake SDK/JDK command output is presented as runtime proof.
- `discovery/` is a synthetic set/catalog designed to expose cross-band selection.
  The selected SDK band is `10.0.200`, the Android manifest band is `9.0.100`,
  and the wrong-band catalog entry is a deliberate decoy. These are not asserted
  to be the contents of an actual released workload set.
- Dependency package objects model the schema inspected in public NuGet packages
  `Microsoft.NET.Sdk.Android.Manifest-9.0.100` version `35.0.50` and
  `Microsoft.NET.Sdk.Android.Manifest-10.0.100` version `36.1.12`.
  Objects use `sdkPackage.id` and string optional flags; optional system image
  IDs can be host maps. The catalog is reduced, not a full manifest archive.

## Deterministic validation (no paid agent/judge runs)

From the repository root, use Python with PyYAML available:

```console
python tests/dotnet-maui/dotnet-maui-doctor/validate_goldens.py
VALLY_TELEMETRY_OPTOUT=1 node eng/evaluation-tools/vally.mjs lint plugins/dotnet-maui/skills/dotnet-maui-doctor --eval-spec tests/dotnet-maui/dotnet-maui-doctor/eval.yaml --known-domains eng/known-domains.txt --allowed-external-deps eng/allowed-external-deps.txt
python eng/eval-quality/check_eval_quality.py
```

The replay script removes only prompt graders from a derived local spec, uses the
production Vally oracle with explicit project-local workspaces, then cleans up.
Its process-local scratch directory stays inside the suite; a Git discovery
ceiling prevents nested replay workspaces from resolving against the parent
repository and silently skipping golden patches.
It checks all golden responses, executes the offline resolver including changed
and malformed inputs, compares the entire protected file set, and rejects five
mutations (modified/deleted no-op inputs, wrong band, optional package inclusion,
and pin rewrite). It never installs SDKs, workloads or JDKs and does not build MAUI.
It also replays five equivalent answers through shipping Vally, reproduces their
prior lexical false negatives, and rejects five missing-evidence mutations.
These checks protect equivalent wording, not semantic correctness of every answer.
One retained plugin answer proposed checks "inside the repo" with Markdown
`` `cd` ``; the former directory-wording proxy incorrectly rejected it.
The retained Luna Apple plan named "Apple's Developer Downloads" with a
possessive apostrophe and bold text; the old literal-adjacency pattern rejected
that correct official source. Both ASCII and typographic apostrophes are accepted.
Repair judging is grounded in the tagged .NET 10 implementation and CLI help:
repair uses current resolved manifests and has no `--skip-manifest-update` option.
Missing that unsupported flag is not a legitimate grading failure. Repair can
run workload garbage collection, so selective-mutation guarantees are not valid.
The incomplete-Java case uses semantic judging rather than requiring a particular
adjective before "error"; an actual correct request for the first actionable Java
error failed that vocabulary proxy. Its judge still checks evidence limits,
compatibility claims and advice-only restraint.
Free replay checks only response completion for that advisory case; it does not
claim to replace the semantic judge.
All fixture files must be staged by the coordinator before the tracking gate passes.

The golden patch supplies one valid implementation, not a required technique.
Alternative resolvers pass if their outputs and preservation behavior meet the
same deterministic contract. Prompt rubrics cover semantic restraint where a
substring or command ban would wrongly reject valid advice.

No fresh model execution or cross-family comparison is implied by these checks.
Interpret separately collected official quality, activation and usage results
before claiming improvement or token/time/cost neutrality.
