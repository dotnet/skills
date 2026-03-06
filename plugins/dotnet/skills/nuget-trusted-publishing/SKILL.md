---
name: nuget-trusted-publishing
description: >
  Set up NuGet trusted publishing (OIDC) on a GitHub Actions repo — replaces long-lived API keys
  with short-lived tokens. USE FOR: trusted publishing, NuGet OIDC, keyless NuGet publish,
  migrate from NuGet API key, NuGet/login, secure NuGet publishing.
  DO NOT USE FOR: publishing to private feeds or Azure Artifacts (OIDC is nuget.org only).
  INVOKES: shell (powershell or bash), edit, create, ask_user for guided repo setup.
---

# NuGet Trusted Publishing Setup

Set up [NuGet trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing) on a GitHub Actions repo. Replaces long-lived API keys with OIDC-based short-lived tokens — no secrets to rotate or leak.

## Prerequisites

- **GitHub Actions** — this skill covers GitHub Actions setup only
- **nuget.org account** — the user needs access to create trusted publishing policies

## When to Use This Skill

Use this skill when:
- Setting up trusted publishing for a NuGet package
- Migrating from `secrets.NUGET_API_KEY` to OIDC-based publishing
- Asked about keyless or secure NuGet publishing
- Creating a new NuGet publish workflow from scratch
- Asked to "remove NuGet API key" or "use NuGet/login"
- Setting up publishing for a dotnet tool, MCP server, or template package
- Asked about `NuGet/login@v1` or `id-token: write`

## Safety Rules

> ⚠️ **Bail-out rule**: If any phase fails after one fix attempt on an infrastructure/auth issue, stop and ask the user. Don't loop on environment problems.

> ⚠️ **Never delete or overwrite without confirmation**: Removing API key secrets, deleting tags/releases, removing workflow steps, or changing package IDs. NuGet package IDs are permanent — mistakes can't be undone.

## Process

> **Fast-path for greenfield repos**: When the user has a simple setup (one packable project, no existing publish workflow), don't gate on multi-turn assessment. Combine phases: create the workflow immediately, include nuget.org policy guidance, local pack recommendation, and filename-matching warning all in one response. The full phased process below is for complex or migration scenarios.

### Phase 1: Assess

Inspect the repo and report findings before making any changes.

1. **Find and classify packable projects** — check `.csproj` files **and `Directory.Build.props`** (package metadata is often set repo-wide). Classify in this order (earlier matches win):
   - `<PackageType>Template</PackageType>` → **Template**
   - `<PackageType>McpServer</PackageType>` → **MCP server** (also a dotnet tool)
   - `<PackAsTool>true</PackAsTool>` → **Dotnet tool**
   - Class library (`IsPackable=true` or no `OutputType`) → **Library**
   - `<OutputType>Exe</OutputType>` with `<IsPackable>true</IsPackable>` → **Application package** (not a tool, but still publishable)
   - `<OutputType>Exe</OutputType>` without `PackAsTool` or `IsPackable` → Not packable by default (ask user if they intend to publish it)

2. **Validate structure** for each project's type:

   | Type | Required |
   |------|----------|
   | All | `PackageId`, `Version` (in .csproj or Directory.Build.props) |
   | Dotnet tool | `PackAsTool` (required); `ToolCommandName` (optional but recommended — defaults to assembly name) |
   | MCP server | `PackageType=McpServer`, `.mcp/server.json` included in package |
   | Template | `PackageType=Template`, `.template.config/template.json` under content dir |

3. **Find existing publish workflows** in `.github/workflows/` — look for `dotnet nuget push`, `nuget push`, or `dotnet pack`.

4. **Check version consistency** — for MCP servers, verify `.csproj` `<Version>` matches both `server.json` version fields (root `version` and `packages[].version`). Flag any mismatch.

5. **Report findings** to the user: classification, missing properties, version mismatches, existing workflows. For multi-project repos, note whether one workflow or separate workflows per package are needed. Offer to fix gaps — use `ask_user` before modifying project files.

> ❌ See [references/package-types.md](references/package-types.md) for per-type details and required properties.

### Phase 2: Local Verification

Pack and verify locally before touching nuget.org — publishing errors waste a permanent version number.

> ⚠️ **Always mention this step**, even if you defer running it. Tell the user: "Before your first publish, run `dotnet pack -c Release -o ./artifacts` to verify the .nupkg is created correctly."

1. `dotnet pack -c Release -o ./artifacts` — verify `.nupkg` is created
2. For tools/MCP servers: install from `./artifacts`, run `--help`, uninstall
3. For libraries: inspect the `.nupkg` contents (it's a zip)

### Phase 3: nuget.org Policy

This phase requires the user to act on nuget.org — guide them with exact values.

1. Determine the **repo owner**, **repo name**, and the **workflow filename** that will publish.

   > ❌ The policy requires the **exact workflow filename** (e.g., `publish.yml` or `publish.yaml`) — just the filename, no path prefix. Matching is case-insensitive. Don't use the workflow `name:` field.

2. Guide the user to create the trusted publishing policy:
   > Go to [**nuget.org/account/trustedpublishing**](https://www.nuget.org/account/trustedpublishing) → **Add policy**
   >
   > - **Repository Owner**: `{owner}`
   > - **Repository**: `{repo}`
   > - **Workflow File**: `{filename}.yml`
   > - **Environment**: `release` *(only if the workflow uses `environment:`; leave blank otherwise)*

   Policy ownership: the user chooses individual account or organization. Org-owned policies apply to all packages owned by that org.

   For **private repos**: policy is "temporarily active" for 7 days — becomes permanent after the first successful publish.

3. Guide the user to create a **GitHub Environment** (recommended but optional — provides secret scoping + approval gates):
   > Repo **Settings** → **Environments** → **New environment** → `release`
   >
   > Add environment secret: **Name** = `NUGET_USER`, **Value** = nuget.org username (NOT email)

   Optional: add **Required reviewers** for an approval gate.

> ⚠️ Wait for the user to confirm they've created the policy **before asking them to remove old API keys/secrets or before attempting to run/publish with the workflow**. Drafting or showing the workflow file itself is OK before confirmation.

### Phase 4: Workflow Setup

Create or modify the publish workflow. **The workflow must always be created or shown in your response** — you may draft/show it even if the nuget.org policy is not yet confirmed, but do not guide the user to actually run/publish or remove old secrets until after confirmation.

**Greenfield**: Create `publish.yml` from the template in [references/publish-workflow.md](references/publish-workflow.md). Adapt .NET version, project path, and environment name. Ensure your output explicitly mentions `id-token: write` and `NuGet/login@v1`.

**Migration** (existing workflow with API key): Modify in place —

1. **Add OIDC permission and environment** to the publishing job:
   ```yaml
   jobs:
     publish:
       environment: release
       permissions:
         id-token: write     # Required — without this, NuGet/login fails with 403
         contents: read      # Explicit — setting permissions overrides defaults
   ```

2. **Add the NuGet login step** before push:
   ```yaml
   - name: NuGet login (OIDC)
     id: login
     uses: NuGet/login@v1
     with:
       user: ${{ secrets.NUGET_USER }}  # nuget.org profile name, NOT email
   ```

3. **Replace the API key** in the push step:
   ```yaml
   --api-key ${{ steps.login.outputs.NUGET_API_KEY }} --skip-duplicate
   ```

4. **Verify**: Ask the user to trigger a publish and confirm the package appears on nuget.org.

> ❌ **Don't delete the old API key secret** until trusted publishing is verified. Removing it is a one-way door — wait for confirmation.

## Troubleshooting

| Problem | Cause | Fix |
|---------|-------|-----|
| `NuGet/login` 403 | Missing `id-token: write` | Add to job permissions |
| "no matching policy" | Workflow filename mismatch | Verify exact filename on nuget.org |
| Push unauthorized | Package not owned by policy account | Check policy owner on nuget.org |
| Token expired | Login step >1hr before push | Move `NuGet/login` closer to push |
| "temporarily active" policy | Private repo, first publish pending | Publish within 7 days |
| `already_exists` on push | Re-running same version | Add `--skip-duplicate` |
| GitHub Release 422 | Duplicate release for tag | Delete conflicting release (confirm first) |
| Re-run uses wrong YAML | `gh run rerun` replays original commit's YAML | Delete obstacle, re-run — never re-tag |

> ⚠️ If any blocker persists after one fix attempt, **stop and ask the user**.

## References

- **Package type details**: [references/package-types.md](references/package-types.md) — detection logic, required properties, minimal .csproj examples
- **Publish workflow template**: [references/publish-workflow.md](references/publish-workflow.md) — complete tag-triggered workflow ready to adapt
- **Microsoft docs**: [NuGet Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
