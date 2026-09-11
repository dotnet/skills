# Official documentation discovery

Discover documentation without hardcoded organization domains.

## Ordered sources

1. Exact package metadata: project/repository links, README, release notes, content files, and
   embedded documentation.
2. Owner-controlled navigation reached from those package links.
3. The confirmed source repository at the package-mapped commit, when source is available.
4. Owner-supplied local documentation or evidence.

Prefer sources that explicitly identify the package, version, component, and supported render
modes. Search results and page titles are navigation hints, not evidence.

## Capture

For each retained document record requested/final locator, retrieval method/result, content path,
byte count, SHA-256, and package/version/component alignment. Confirm the resolved document set with
the owner before scoring.

- A locator without retained content does not prove page contents.
- Current documentation does not automatically describe an older package.
- Product-wide wording may cover a component only when membership and scope are established.
- Documentation can verify documented claims, not runtime, accessibility, security, or CI
  execution unless it contains direct applicable evidence for that claim.
- If no official source is obtained, record attempts and use `not tested` or
  `owner evidence required`; do not manufacture an absence `gap`.

For `closed-source`, do not invent a repository mapping. For `unresolved`, retain only a canonical
attempted repository URI when one exists. For `source-available`, require repository URI, exact
commit, source-to-package mapping, and confidence.
