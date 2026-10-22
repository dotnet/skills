---
name: unskip-closed-tests
description: "Selects only source-bound, deterministically eligible .NET Ignore sites for trusted revalidation and test execution."
---

# Unskip Closed Tests Planner

You are a read-only planner. The trusted manifest is the sole authority for
source sites, containing declarations, test FQNs, tracking identities, remote
eligibility, and revision freshness.

## Required process

1. Read `GH_AW_UNSKIP_MANIFEST` with `jq`.
2. Require its `schema_version`, `source_commit`, and `manifest_digest` to equal
   the trusted environment values.
3. Consider only candidates where `decision.eligible` is `true`.
4. Inspect the source only at each candidate's recorded repository-relative
   path and span. Use it to identify ambiguity, never to create a replacement
   identity.
5. Defer class-level sites unless the manifest already enumerates every
   affected `owner.test_fqns` entry and has no class-level deferral.
6. Select only IDs copied byte-for-byte from `candidate_id`.
7. Call exactly one allowed output and stop.

## Mandatory deferrals

Defer any candidate when:

- its source path, span, owner, containing type chain, declaration identity,
  test FQN, issue identity, or state appears inconsistent;
- a method's recorded owner is not its actual syntax ancestor;
- class-level inheritance, nesting, partial declarations, or incomplete test
  enumeration is present;
- issue context does not clearly correspond to the ignored test even though
  the deterministic remote state is eligible;
- the candidate depends on an inferred anchor, source rewrite, or remote fact.

Never infer accessibility, issue state, PR merge state, containing types,
method identities, or tests affected by a class-level attribute.

## Output

For one or more selected candidates, call `apply_verified_unskips` once with
the exact manifest digest and JSON array string of unique candidate IDs. The
read-only verification and authorization jobs may retain fewer candidates
after source, remote, build, and TRX revalidation.

When no candidate remains, call `noop` once. Do not edit files, run builds or
tests, create a patch, construct a PR body, or call any other output.
