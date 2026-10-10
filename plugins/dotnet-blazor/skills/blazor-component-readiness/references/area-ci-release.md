# CI, documentation, and release validation

Applies to `CI-*`.

For a component's remaining release-reverification requirement, inspect only
evidence applicable to that component. The package pipeline audit below belongs
to separately requested package work, not a component prerequisite.

Trace pull-request validation and release separately through source/dependency acquisition,
restore/build/test/package/browser checks, accessibility/security gates, immutable artifact
handoff, signing, SBOM/provenance generation, release, retained evidence, and revalidation.

Collect the exact workflow revision used for the assessed package, required-check evidence when
available, applicable release-check results, artifact digests before
and after privileged stages, and requirement-mapped release results.

Workflow configuration shows intent. Successful required execution against the exact artifact is
evidence. A passing build does not prove runtime, accessibility, or package integrity. Current
default-branch automation does not prove an older release.

Use `owner evidence required` for inaccessible protection/environment/release records. Use `gap`
for an observable missing required public release control, rebuilding after verification, or
signing/releasing different bytes than were validated.

For `CI-07`, inspect the actual release job graph and each job's permissions, environment, secrets,
and executable source steps. A separate pull-request workflow or an environment approval does not
establish isolation when one privileged job checks out source, restores dependencies, runs build
scripts, signs, and publishes. Treat that same-job execution as a `gap`: job-level credentials and
OIDC permissions are available to the untrusted build steps even when authentication is performed
later.

For `CI-08`, path equality and in-place ordering are not an immutable handoff. Verification must
bind an artifact digest, or an equivalent byte identity guarantee, across the boundary into the
privileged publishing stage. Signing, verifying, and pushing the same mutable workspace path
without that boundary is a `gap`; rebuilding, repacking, or permitting mutation after verification
also invalidates the handoff.

Do not score separate obligations for per-defect regression coverage, browser
test infrastructure, additional accessibility cadence, documentation assertions
or assessor tool prerequisites. Existing render-mode, sample-discovery and named
accessibility requirements remain. Use relevant existing tests/results as
evidence without imposing another testing-policy deliverable.

Check operation-specific tools internally before using them. Missing assessor
SDKs, browsers or workloads are probe blockers, not vendor defects. Do not install
tools or execute assessed inputs without permission and required isolation.
Demonstrably incorrect vendor setup instructions can be reported on their own
evidence; absence of an assessor prerequisite is not that evidence.

A release checklist or completed release execution may be private governance evidence unless the
row explicitly requires publication. Public absence alone therefore routes to
`owner evidence required`, not automatically `gap`.

## Requested implementation guidance

Use [release remediation patterns](remediation-guidance.md#release-patterns) only for requested
unresolved findings in the existing validated revision. `CI-05` required gates, `CI-07` privilege
separation, and `CI-08` immutable handoff remain distinct **versioned extensions**.
For `CI-08`, distinguish unsigned build/transfer identity from signed final identity: signing can
change bytes. Verify the author-signed artifact and require its digest to equal the upload bytes,
not the unsigned build digest. NuGet repository signing or
countersigning can change distribution bytes, so do not require raw upload/distribution equality.
Retain both digests and verify correspondence through the preserved author signature and signed
content, the repository signature or countersignature, and package ID/version. Do not treat a
repository-signing transformation alone as a failure, or missing correspondence as verified.
No pattern guarantees acceptance, authorizes execution or changes the assessed result.
