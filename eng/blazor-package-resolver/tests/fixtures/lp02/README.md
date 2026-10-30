# LP-02 minimum v1 contract

## Purpose and stage boundary

`lp02-public-source/1.0.0` establishes a **qualified public-source-availability
fact for the exact checked package**. Only `public-source-observed` is
positive. Its finding states the package-declared repository, exact commit
and capture time, the anonymous retrieval and bounded library-source mapping,
and explicitly says that this does **not** establish that the published
binaries were built from that revision.

GitHub codeload can serve fork-network commit objects through the declared
repository URL. Successful retrieval therefore does **not** prove that the
commit belongs to that repository's branches or tags. The positive finding
states this qualification; LP-02 does not perform a network membership check.

Do not infer repository ownership, complete build closure, reproducible
builds, binary/source correspondence, license compliance or overall
readiness. Stronger correspondence/provenance belongs to LP-10/PI-11.
No release tag, private-source conclusion, build reproduction, embedded
source-repository manifest or caller acceptance boolean is part of LP-02.
The copied rubric/basis remain byte-pinned; this is the qualified operational
contract for this handler, not a rewrite of those source documents.

Acquisition may retrieve source; the deterministic resolver is **offline**.
It only consumes `CheckedInput` bytes already SHA-256-validated by dispatch.
It does not reopen paths, inspect Git state, read credentials, execute code,
invoke `gh`, evaluate MSBuild or access the network.

## Supported package and source profile

- Ordinary, bounded NuGet ZIPs: at most 64 MiB compressed and 64 MiB total
  uncompressed, 4096 entries, 8 MiB central directory, and safe ASCII relative
  entry paths of at most 256 characters. Reject dot/dot-dot segments,
  backslashes, absolute paths, case aliases, symlinks, encryption, Zip64,
  unsupported flags/methods, inconsistent headers/lengths/CRCs and unsupported
  archive framing. ZIP storage and normal Deflate use the existing bounded
  archive validator, not a new codec or decompressor. The standard root
  NuGet packaging entry `[Content_Types].xml` is explicitly supported.
- Exactly one root nuspec, at most 1 MiB. Reuse the recognized NuGet namespace
  and metadata-sibling rules. Accept strict UTF-8 (optional BOM) or BOM-marked
  UTF-16 LE/BE; if present, the XML encoding declaration must say `utf-8`
  or `utf-16` respectively, case-insensitively. DTDs/entities are prohibited.
  Exactly one nonblank, simple direct ASCII id/version (at most 256 characters)
  and no ambiguous repository
  element may supply identity. Repository attributes are limited to unqualified
  `type`, `url`, `commit`, `branch`; URL/commit must both be nonblank.
- Repository profile: `https://github.com/{owner}/{repo}`, optionally normalizing
  a terminal `.git`. No userinfo, query, fragment, port, trailing slash,
  arbitrary host, path encoding or ref/tag lookup. Owner/repository use the
  documented implementation's bounded ASCII GitHub-name pattern. Commit is
  a full nonzero 40-hex SHA-1, normalized to lowercase. Other object formats
  and environments are unresolved, not evidence of closed source.
- Retain the complete bounded codeload ZIP. All files must be under the
  `{repo}-{commit}/` root (case-insensitive root comparison). Select exactly
  one `.csproj` project marker and one `.cs` implementation, each at most
  1 MiB, with exact archive paths and SHA-256 digests. Source ZIPs may have
  GitHub's 40-byte exact-commit archive comment (or no comment); arbitrary
  comments are unsupported. Package ZIPs remain comment-free, preserving
  LP-01's existing validator behavior.
- The project must be strict UTF-8 XML (optional single UTF-8 BOM) with unqualified `Project`,
  `Sdk="Microsoft.NET.Sdk"`, and one unconditional literal
  direct top-level `PropertyGroup/PackageId` matching the package ID
  case-insensitively. Compare guarded MSBuild element/attribute/property
  names case-insensitively. Reject `Choose`, `Target`, any conditional
  attribute, nested/noncanonical or competing `PackageId` declarations,
  explicit compile/import/SDK-import nodes, disabled default compile/items,
  and any `DefaultItemExcludes` declaration. Also reject when
  `Directory.Build.props` or `Directory.Build.targets` exists in any selected
  project ancestor directory inside the retained archive, including the
  project's own directory and archive root (case-insensitive path comparison).
  Do not evaluate those files or MSBuild expressions.
  The implementation must be nonblank strict UTF-8 under that project's
  directory, outside `obj`/`bin`. A nonblank ASCII rationale of at most
  1000 characters explains the mapping. This is bounded file/marker evidence,
  **not** C# compilation, full inclusion analysis or recursive import closure.

Outside-profile content escalates conservatively. Source trees that need
inferred package IDs, other project languages, conditional properties or
arbitrary build evaluation are intentionally unsupported in v1.

## Acquisition procedure

Run the separate acquisition command from the repository root. Supply exact
archive paths, including the commit root, and a short mapping rationale:

```powershell
dotnet run --project eng\blazor-package-resolver\BlazorPackageResolver.csproj -- acquire-lp02 package.nupkg capture-new repo-COMMIT/src/Library/Library.csproj repo-COMMIT/src/Library/Widget.cs "The literal PackageId identifies this library; Widget.cs is adjacent library implementation."
```

The command reads and validates the package before constructing the one fixed
URL `https://codeload.github.com/{owner}/{repo}/zip/{commit}`. Its dedicated
HTTP handler disables default/explicit credentials, cookies, proxy discovery,
redirects and automatic decompression; it supplies only a fixed user agent.
It does not use browser state, `gh`, Git helpers, API tokens or ambient
application authentication. Normal TLS certificate validation and revocation
checks remain enabled. Environments requiring proxies or credentials are
unsupported; do not weaken the transport to obtain a positive.

One GET, no retries or fallback hosts, 60-second total timeout, 16 KiB
response-header setting and 64 MiB streamed response cap bound retrieval.
Only HTTP 200 without Content-Encoding retains `source.zip`.
404 -> `not-found`; 401/403 -> `auth-required`; 429/451 -> `blocked`;
5xx/network/timeout -> `unavailable`; redirects, other statuses, encoded or
oversized responses -> `unsupported`. Every such result generates
`observation.json`, with no success capture/inventory for unresolved attempts.
The destination must not already exist. Filesystem failures are CLI errors,
not successful captures. Preserve the generated receipt and archive together;
do not hand-author an access assertion.

The command hashes source files found at the supplied paths; it does not
force them to be relevant. Missing/unsupported source selections cannot
produce a resolver positive. Retaining a successful HTTP response alone is
insufficient.

## Observation schema and checked bindings

`observation.json` is strict UTF-8 JSON, maximum 16 KiB and depth 8, with
duplicate/unknown fields rejected. There are no acceptance booleans.
Malformed UTF-8, escaped Unicode or deferred JSON string/field decoding
failures produce `observation-invalid`, never an unhandled exception.

```json
{
  "schema_version": 1,
  "producer": "lp02-anonymous-github/1.0.0",
  "package_id": "Sample.Library",
  "package_version": "1.0.0",
  "package_sha256": "<64 lowercase hex>",
  "repository_url": "https://github.com/owner/repo",
  "commit": "<40 lowercase hex>",
  "retrieval_attempt": {
    "url": "https://codeload.github.com/owner/repo/zip/COMMIT",
    "observed_at_utc": "2026-10-30T12:00:00Z",
    "transport": "direct-https-no-credentials/1",
    "result": "retrieved",
    "http_status": 200
  },
  "capture_receipt": {
    "role": "source-archive",
    "sha256": "<64 lowercase hex>"
  },
  "source_inventory": [
    {"kind": "project", "path": "repo-COMMIT/src/Library.csproj", "sha256": "<64 lowercase hex>"},
    {"kind": "implementation", "path": "repo-COMMIT/src/Widget.cs", "sha256": "<64 lowercase hex>"}
  ],
  "mapping_rationale": "Short bounded source mapping explanation."
}
```

Use checked roles `package`, `source-observation`, `source-archive` in the
existing schema-version-1 replay suite; pin each file's path and SHA-256.
The receipt binds exact package ID/version/digest, normalized repository,
commit and codeload URL; the capture digest binds the retained archive;
the inventory binds unique selected paths/kinds and their actual bytes.
An unresolved attempt instead has `capture_receipt: null` and an empty
inventory; the status must match its result. A transport failure has
`http_status: null`. UTC times must use `yyyy-MM-ddTHH:mm:ssZ`.

These are machine-generated acquisition observations at a checked-evidence
boundary, **not signed attestations**. Offline hash checks cannot prove that
a dishonest party did not fabricate a receipt/archive or independently attest
the transport's history. Use the supplied acquisition operation and preserve
its output; never substitute a hand-authored `public=true`, correspondence
assertion or a repository-embedded `.lp02-source.json`.

## Ordered branches and status

| Branch | Meaning |
|---|---|
| `missing-package` | Exact package absent |
| `package-unreadable` | Package/identity outside the safe unambiguous profile |
| `source-declaration-missing` | Package has no complete URL/commit declaration |
| `public-retrieval-unresolved` | Declaration outside retrieval profile, or retained attempt unresolved |
| `observation-missing` | Required checked observation or successful attempt's archive absent |
| `observation-invalid` | Unsupported/malformed receipt, inventory or source archive |
| `observation-binding-conflict` | Exact package/revision/archive/selected-byte bindings disagree |
| `source-relevance-unestablished` | Selected files/marker/rationale do not establish the bounded mapping |
| `public-source-observed` | The sole qualified positive |

Validation order follows package -> declaration/profile -> observation shape
-> package/retrieval binding -> attempt state -> archive binding/profile ->
selected file bindings -> relevance. Every nonpositive result is an
escalation with unknown applicability and no factual finding: unresolved /
`not tested`, **not** closed-source or a gap.

## Focused validation and retained replay

There are **48 named synthetic cases**: 33 frozen oracle/replay cases and
15 runtime cases covering generated anonymous observations, unresolved HTTP
states, network failure, header/stream caps, receipt/file/archive resource
boundaries, path/root safety, mapping, duplicate inventory, checked-buffer/
culture determinism, unsupported case-insensitive MSBuild/identity/import/
exclusion shapes, malformed receipt Unicode and the project UTF-8 BOM.
Runtime cases group related boundary/status assertions;
they are not an exhaustive cross-runtime differential matrix. The independent
Python oracle specifies expected branches and bytes without importing or
invoking production code. The ordinary tests never access the network.

```powershell
python eng\blazor-package-resolver\tests\lp02_expectations.py
dotnet run --project eng\blazor-package-resolver\tests\ResolverChecks.csproj -- lp02
python eng\blazor-package-resolver\tests\lp01_expectations.py
dotnet run --project eng\blazor-package-resolver\tests\ResolverChecks.csproj
dotnet run --project eng\blazor-package-resolver\BlazorPackageResolver.csproj -- coverage
```

`--write` on the Python oracle materializes its specified synthetic inputs
and expectations, never production results. `acceptance.json` separately
pins the two vendor packages using the already accepted LP-01 retained
identities. Supply both `LP02_SYNCFUSION_NUPKG` and
`LP02_INFRAGISTICS_NUPKG` to run actual retained replay. The runner verifies
their exact byte counts/digests, stages only those supplied bytes, and replays
the frozen expectations. The binaries remain outside Git. Both expected
outcomes are `observation-missing`, qualified **declared-source-unverified**,
until real checked anonymous acquisition evidence and relevance are supplied.
Do not infer a positive from their repository declarations.
