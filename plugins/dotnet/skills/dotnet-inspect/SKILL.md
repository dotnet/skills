---
name: dotnet-inspect
version: 0.6.8
description: Query .NET APIs across NuGet packages, platform libraries, and local files. Search for types, list API surfaces, compare and diff versions, find extension methods and implementors. Use whenever you need to answer questions about .NET library contents.
---

# dotnet-inspect

Query .NET library APIs — the same commands work across NuGet packages, platform libraries (System.*, Microsoft.AspNetCore.*), and local .dll/.nupkg files.

## Quick Decision Tree

- **Code broken?** → `diff --package Foo@old..new` first, then `member`
- **What types exist?** → `type --package Foo` (discover types in a package or library)
- **What members does a type have?** → `member Type --package Foo` (compact table by default)
- **What does a type look like?** → `type Type --package Foo` (tree view for single type)
- **What are the method signatures?** → `member Type --package Foo -m Method` (full signatures + docs)
- **What is the source/IL?** → `member Type --package Foo -m Method:1 -v:d` (Source, Lowered C#, IL)
- **Where is the source code?** → `source Type --package Foo` (SourceLink URLs), `source Type Member` (with line numbers)
- **What constructors exist?** → `member 'Type<T>' --package Foo -m .ctor` (use `<T>` not `<>`)
- **How many overloads?** → `member Type --package Foo --show-index` (shows `Name:N` indices)
- **What does this package depend on?** → `depends --package Foo`
- **What does this type inherit?** → `depends 'INumber<TSelf>'`
- **What metadata fields exist?** → `-S Section --fields "PDB*"` (structured query, no DSL)
- **What version is available?** → `Foo --version` (cache-first), `Foo --latest-version` (always NuGet), `Foo --versions` (list all)

## When to Use This Skill

- **"What types are in this package?"** — `type` discovers types, `find` searches by pattern
- **"What members does this type have?"** — `member` for methods/properties/events (docs on by default)
- **"What changed between versions?"** — `diff` classifies breaking/additive changes
- **"This code uses an old API — fix it"** — `diff` the old..new version, then `member` to see the new API
- **"What extends this type?"** — `extensions` finds extension methods/properties (`--reachable` for transitive)
- **"What implements this interface?"** — `implements` finds concrete types
- **"What does this type depend on?"** — `depends` walks type hierarchy, package deps, or library refs
- **"Where is the source code?"** — `source` returns SourceLink URLs; add member name for line numbers
- **"What version/metadata does this have?"** — `package` and `library` inspect metadata
- **"What version is available?"** — `Foo --version` (fast, cache-first — like `docker run`)
- **"What's the latest on NuGet?"** — `Foo --latest-version` (always queries NuGet — like `docker pull`)
- **"What versions exist?"** — `Foo --versions` (list all published versions)
- **"What TFMs are available?"** — `package Foo --tfms`, then `type --package Foo --tfm net8.0`
- **"Show me something cool"** — `demo` runs curated showcase queries

## Key Patterns

Default output is compact columnar tables (like `docker images` or `git log --oneline`). No flags needed for scanning:

```bash
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json    # scan members
dnx dotnet-inspect -y -- type --package System.Text.Json                     # scan types
dnx dotnet-inspect -y -- diff --package System.CommandLine@2.0.0-beta4.22272.1..2.0.3  # triage changes
```

Four formatters: **oneline** (default), **plaintext**, **markdown** (`-v` or `--markdown`), **json** (`--json`). Verbosity (`-v:q/m/n/d`) controls which sections are included; formatter controls how they render. They compose freely — except `--oneline` and `-v` cannot be combined.

```bash
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json -v:m  # markdown with docs
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json -v:d  # detailed (source/IL)
dnx dotnet-inspect -y -- System.Text.Json -v:n --plaintext                      # all local sections, plaintext
```

Use `diff` first when fixing broken code — triage changes, then drill into specifics:

```bash
dnx dotnet-inspect -y -- diff --package System.CommandLine@2.0.0-beta4.22272.1..2.0.3  # what changed?
dnx dotnet-inspect -y -- member Command --package System.CommandLine@2.0.3               # new API surface
```

## Version Resolution (Docker-style)

Version queries use Docker-like semantics: cached packages are served in under 15ms, network calls cost 1–4 seconds. Three flags, three behaviors:

| Flag | Behavior | Network | Like Docker... |
| ---- | -------- | ------- | -------------- |
| `--version` (bare) | **Local** — returns the version from local cache | Only on cache miss | `docker run nginx` |
| `--latest-version` | **Remote** — queries nuget.org for the absolute latest | Always | `docker pull nginx` |
| `--versions` | **Remote** — returns every published version | Always | `docker image ls --all` |

`--version` and bare-name inspection share the same cache. If `Foo --version` returns `2.0.3`, then `Foo` (or `package Foo`) will inspect that same `2.0.3` — no surprises, no extra network call. This is the fast path for most tasks.

`--latest-version` and `--versions` always query nuget.org, so they reflect the latest published state. Use `--latest-version` when you need to confirm the newest version, e.g., before a dependency upgrade.

```bash
dnx dotnet-inspect -y -- Foo --version           # what's in the cache? (fast, local)
dnx dotnet-inspect -y -- Foo --latest-version     # what's on nuget.org? (always network)
dnx dotnet-inspect -y -- Foo --versions           # list all published versions
dnx dotnet-inspect -y -- Foo --versions 5         # list latest 5 versions
dnx dotnet-inspect -y -- Foo --versions --preview # include prerelease versions
```

The same flags work on the `package` subcommand:

```bash
dnx dotnet-inspect -y -- package Foo --version           # same local cache check
dnx dotnet-inspect -y -- package Foo --latest-version     # always queries nuget.org
dnx dotnet-inspect -y -- package Foo --versions           # list all versions
```

Version pinning with `@version` syntax:

```bash
dnx dotnet-inspect -y -- Foo@2.0.3                # pinned — no network if cached
dnx dotnet-inspect -y -- Foo@latest               # always checks nuget.org
dnx dotnet-inspect -y -- Foo                      # prefer cache, refresh on TTL expiry
```

**Use `--version` (not `--latest-version`) as the default.** It's fast and returns the same version that bare-name commands will use. Only reach for `--latest-version` when you need the absolute latest from nuget.org.

## Structured Queries (like Go templates, without a DSL)

Discover the schema, then select and project — no template language needed:

```bash
dnx dotnet-inspect -y -- System.Text.Json -D                          # list sections
dnx dotnet-inspect -y -- System.Text.Json -D --effective              # sections with data (dry run)
dnx dotnet-inspect -y -- library System.Text.Json -D --tree           # full schema tree
dnx dotnet-inspect -y -- System.Text.Json -S Symbols                  # render one section
dnx dotnet-inspect -y -- System.Text.Json -S Symbols --fields "PDB*"  # project specific fields
dnx dotnet-inspect -y -- type System.Text.Json --columns Kind,Type    # project specific columns
```

## Search Scope

Search commands (`find`, `extensions`, `implements`, `depends`) use scope flags:

- **(no flags)** — all platform frameworks (runtime, aspnetcore, netstandard)
- **`--platform`** — all platform frameworks
- **`--extensions`** — curated Microsoft.Extensions.* packages
- **`--aspnetcore`** — curated Microsoft.AspNetCore.* packages
- **`--package Foo`** — specific NuGet package (combinable with scope flags)

`type`, `member`, `library`, `diff` accept `--platform <name>` as a string for a specific platform library.

## Command Reference

| Command | Purpose |
| ------- | ------- |
| `type` | **Discover types** — terse output, no docs, use `--shape` for hierarchy |
| `member` | **Inspect members** — docs on by default, supports dotted syntax (`-m Type.Member`) |
| `find` | Search for types by glob or fuzzy match across any scope |
| `diff` | Compare API surfaces between versions — breaking/additive classification |
| `extensions` | Find extension methods/properties for a type (`--reachable` for transitive) |
| `implements` | Find types implementing an interface or extending a base class |
| `depends` | Walk dependency graphs upward — type hierarchy, package deps, or library refs |
| `package` | Package metadata, files, versions, dependencies, `search` for NuGet discovery |
| `library` | Library metadata, symbols, references, SourceLink audit |
| `source` | **SourceLink URLs** — type-level or member-level (with line numbers), `--verify` to check URLs |
| `demo` | Run curated showcase queries — list, invoke, or feeling-lucky |

## Filtering and Limiting

```bash
dnx dotnet-inspect -y -- type System.Text.Json -k enum               # filter by kind (type and member commands)
dnx dotnet-inspect -y -- type System.Text.Json -t "*Converter*"      # glob filter on type names
dnx dotnet-inspect -y -- member System.Text.Json JsonDocument -m Parse  # filter by member name
dnx dotnet-inspect -y -- type System.Text.Json -5                    # first 5 lines (like head -5)
```

**Do not pipe output through `head`, `tail`, or `Select-Object`.** Use built-in limiting:

- **`-n N` or `-N`** — line limit (like `head`). Keeps headers, truncates cleanly.
- **`-m N`** (numeric) — item limit (members per kind section).
- **`-k Kind`** — filter by kind: `class/struct/interface/enum/delegate` (type) or `method/property/field/event/constructor` (type single-type view, member).
- **`-S Section`** — show only a specific section (glob-capable).

## Key Syntax

- **Generic types** need quotes: `'Option<T>'`, `'IEnumerable<T>'`
- **Use `<T>` not `<>`** for generic types — `"Option<>"` resolves to the abstract base, `'Option<T>'` resolves to the concrete generic with constructors
- **`type` uses `-t`** for type filtering, **`member` uses `-m`** for member filtering (not `--filter`)
- **Dotted syntax** for `member`: `-m JsonSerializer.Deserialize` or `-m System.Text.Json.JsonSerializer.Deserialize`
- **Diff ranges** use `..`: `--package System.Text.Json@9.0.0..10.0.0`
- **Derived types** only show their own members — query the base type too

## Installation

Use `dnx` (like `npx`). Always use `-y` and `--` to prevent interactive prompts:

```bash
dnx dotnet-inspect -y -- <command>
```

## Full Documentation

For the full mental model, structured queries, and migration workflow:

```bash
dnx dotnet-inspect -y -- llmstxt
```
