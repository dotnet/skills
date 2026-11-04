# Standalone component assessment

A component request selects all 52 current component-specific checks for one
confirmed component. It does not start a package assessment, require a package
revision, or combine package and component results.

Use `assessment init --kind component --component <confirmed-id>` with the final
confirmed inputs. Retain exact package identity and the relevant component,
inherited implementation, documentation and upstream evidence. Shared source
is context for that component, not permission to assess siblings or package
governance, signing, licensing, CI or conditional AI/scaffolder requirements.

The former 51-check V1 profile, its descriptor pair and
`--package-context-revision` are retired. Do not reconstruct them, start a
48-check package run, or fall back to unified output. Noncurrent contracts
are rejected without migrating or altering historical artifacts.

An explicitly supplied current package revision may be optionally bound through
`--package-revision`. Declared references must match the exact package, source
and revision digests throughout validation and delivery. Missing, unrelated,
stale or altered declared bindings fail without falling back to standalone.
With no declared binding, do not discover or infer a package revision.

Component output contains only the selected component's current requirements,
findings and evidence. Keep the complete verification inputs internally.
The reader does not export raw inputs, source/package archives, policy files,
historical ledgers or unrelated attachments merely because they were registered.
The component reader is not a self-contained evidence-validation bundle.

Construct any selected-only evidence companion through the existing evidence
builders. Do not crop and relabel a historical ledger. When selected records
depend on unexported supersession ancestors, retain the evidence view and
explicitly disclose why the structured companion is omitted.

Preserve exact canonical copies, selected record identities and provenance,
per-check qualifications, immutable revisions and no-overwrite protection.
Retain exact feedback for every feedback-bound predecessor. Use `--feedback`
for the current revision and repeatable `--feedback-history` for earlier files;
missing or altered historical commentary blocks verification, never a silent skip.
Neither a scope failure nor an unavailable dependency permits a broader export
or weaker validation.

Follow [the shared assessment workflow](assessment-workflow.md) for assessment
work and [reader delivery](partner-preview.md) only after canonical verification.
An explicitly requested package assessment produces its own separate report.
One completed component does not complete a library.
