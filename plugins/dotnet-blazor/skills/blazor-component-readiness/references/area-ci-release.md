# CI, documentation, and release validation

Applies to `CI-*`.

Trace pull-request validation and release separately through source/dependency acquisition,
restore/build/test/package/browser checks, accessibility/security gates, immutable artifact
handoff, signing, SBOM/provenance generation, release, retained evidence, and revalidation.

Collect the exact workflow revision used for the assessed package, required-check evidence when
available, package-based tests, browser coverage for each claimed render mode, compiling
documentation samples with behavioral assertions, toolchain prerequisites, artifact digests before
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

When this same assessment directly establishes a current product defect and the exact admitted test
inventory does not cover it, the regression-coverage row is a `gap`; do not require a separate
owner-acceptance record to hide the observable missing test. Exact documentation example source
that contains syntax but no behavioral assertion directly establishes the missing-assertion
conjunct even if compilation was not rerun. Bind a `CI-09` direct absence gap with
`public-absence-v1` using a typed `sample-inventory`. If behavioral assertions are present but the
sample compilation itself fails, use `direct-failure-v1` cause `sample-compilation-failed` bound to
a `sample-compilation-result`; do not make a false absence claim. These two directed-gap protocol
families are mutually exclusive for one completed `CI-09` row.

A release checklist or completed release execution may be private governance evidence unless the
row explicitly requires publication. Public absence alone therefore routes to
`owner evidence required`, not automatically `gap`.
