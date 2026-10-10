# Official documentation discovery

Discover documentation without hardcoded organization domains.

## Ordered sources

1. Exact package metadata: project/repository links, README, release notes, content files, and
   embedded documentation.
2. Owner-controlled navigation reached from those package links, including getting-started,
   hosting and render-mode setup links on the component page. Retain supported-mode declarations
   for BEQ-23; setup guides aid discovery, not a new per-mode sample/execution requirement.
3. The confirmed source repository at the package-mapped commit, when source is available.
4. Owner-supplied local documentation or evidence.

Before deciding BEQ-02/08/23, read and retain the full "Supported render modes" section, or
equivalent supported-mode declaration, in the exact package README and the package-mapped repository
README when available. A prefix read or silent/partial manifest does not establish the
supported-mode set.

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
