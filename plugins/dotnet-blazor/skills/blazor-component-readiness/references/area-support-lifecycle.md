# Support, servicing, and lifecycle

Applies to `SUP-*`.

Collect named package/product support ownership and contact route, response targets, supported
versions, patch cadence, end-of-life policy, public issue route, private security disclosure,
emergency servicing/release ownership, per-release requirement revalidation, and
suspension/recovery expectations.

Most controls are repository-wide; preserve rubric ownership exactly. Component-specific
revalidation must still show that the assigned component was covered.

Public commitments can be `verified`. Private staffing, escalation, incident, release, or
revalidation records are `owner evidence required`. Contradictory or directly missing required
public policy may be a `gap`. Recent issue activity is not an SLA, and a security-policy file alone
does not prove emergency release capability.

For rows that explicitly require a response SLA to be published, security patch cadence to be
documented, or advance EOL notice to be public, a digest-bound complete owner-controlled public
corpus that directly records the commitment as absent is a `gap`. Use `not tested` only when the
public corpus or bounded absence search is incomplete or blocked. Bind direct absence for
`SUP-03`, `SUP-05`, and `SUP-06` with `public-absence-v1` using corpus kind
`public-support-corpus`; free-text search notes do not establish the gap. The typed corpus is
canonical JSON with `owner_controlled: true`, `complete: true`, sorted covered/present marker sets,
and the row's required marker covered but absent.

Ask for evidence owner, location/basename, scope, date, supported versions, response target, and
exact package/candidate applicability. Keep follow-up limited to supplied evidence and retesting the
same confirmed unit.

For `SUP-01`, organization, authorship, repository activity, or a contact route does not establish
accountable support ownership. `verified` requires a canonical `support-ownership-v1` owner record
with the current accountable role and owner, exact package/product scope, backup owner, escalation
path, and effective UTC time. The validator enforces this evidence shape; staffing truth and
organizational authority remain owner attestations.

Register the owner record with its confirmed public/internal owner provenance, the owner-input
basename as locator, and `protocol:support-ownership-v1` as `provenance.method`.
