---
name: nuget-trusted-publishing
description: >
  Set up NuGet trusted publishing (OIDC) on a GitHub Actions repo — replaces long-lived API keys
  with short-lived tokens. USE FOR: trusted publishing, NuGet OIDC, keyless NuGet publish,
  migrate from NuGet API key, NuGet/login, secure NuGet publishing.
  DO NOT USE FOR: publishing to private feeds or Azure Artifacts (OIDC is nuget.org only).
  INVOKES: powershell, edit, create, ask_user for guided repo setup.
---

# NuGet Trusted Publishing Setup

Set up [NuGet trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing) on a GitHub Actions repo. Replaces long-lived API keys with OIDC-based short-lived tokens — no secrets to rotate or leak.

## Prerequisites

- **GitHub Actions** — this skill covers GitHub Actions setup specifically (trusted publishing also supports Azure DevOps, but that requires a different configuration flow)
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

## Process

> ⚠️ **Bail-out rule**: If any phase fails after one fix attempt on an infrastructure/auth issue, stop and ask the user. Don't loop on environment problems.

> ⚠️ **Safety rule**: Never delete, remove, or overwrite anything without explaining the consequences and getting user confirmation first. This includes: removing API key secrets, deleting tags/releases, removing workflow steps, or changing package IDs. NuGet package IDs are permanent — mistakes can't be undone.

### Phase 1: Discovery

Inspect the repo to understand what's being packaged and how.

1. **Find packable projects**: Search for `.csproj` files. Check `IsPackable`, `PackAsTool`, `PackageType`, and `OutputType` properties. Also check `Directory.Build.props` for repo-wide settings.

2. **Classify each project** (check in this order):
   - `<PackageType>Template</PackageType>` → **Template package**
   - `<PackageType>McpServer</PackageType>` → **MCP server** (also a dotnet tool)
   - `<PackAsTool>true</PackAsTool>` → **Dotnet tool**
   - Class library (no `OutputType` or `IsPackable=true`) → **NuGet library**
   - `<OutputType>Exe</OutputType>` without `PackAsTool` → Not a NuGet package, skip

3. **Find existing workflows**: Search `.github/workflows/*.yml` for `dotnet nuget push`, `nuget push`, or `dotnet pack` steps.

4. **Report findings** to the user before proceeding.

> See [references/package-types.md](references/package-types.md) for per-type structural requirements and detection details.

### Phase 2: Structure Validation

Verify the repo has the right MSBuild properties and supporting files for its package type.

| Type | Check for |
|------|-----------|
| All | `PackageId`, `Version` in .csproj or Directory.Build.props |
| Dotnet tool | `PackAsTool`, `ToolCommandName` |
| MCP server | `PackageType=McpServer`, `.mcp/server.json` exists, included in package via `<None Include=".mcp/server.json" Pack="true" .../>` |
| Template | `PackageType=Template`, `.template.config/template.json` exists under content dir |

If anything is missing, offer to add it. Use `ask_user` to confirm before modifying project files.

> ❌ **Don't skip `Directory.Build.props`** — package metadata is often set at the repo root, not in individual .csproj files. Missing it means reporting false negatives.

### Phase 3: Local Pre-Publish Testing

Before configuring nuget.org, verify the package builds and works locally.

1. **Pack**: `dotnet pack -c Release -o ./artifacts`
2. **Verify the `.nupkg`** was created in `./artifacts/`
3. **For dotnet tools / MCP servers** — install from local and test:
   ```bash
   dotnet tool install -g --add-source ./artifacts {PackageId}
   {ToolCommandName} --help   # Verify it runs
   dotnet tool uninstall -g {PackageId}
   ```
4. **For libraries** — verify the package contains expected assemblies:
   ```bash
   dotnet nuget locals all --list  # Note the global-packages path
   # Or unzip the .nupkg (it's a zip) and inspect lib/
   ```

> ❌ **Don't skip local testing** — discovering packaging errors after publishing wastes a version number (nuget.org IDs are permanent).

### Phase 4: nuget.org Policy Setup

This is a manual step — guide the user through it with exact values.

1. Extract repo owner and name from the git remote:
   ```powershell
   git remote get-url origin
   # Parse: https://github.com/{owner}/{repo}.git or git@github.com:{owner}/{repo}.git
   ```

2. Identify the workflow filename (just the filename, not the path) that will do the publishing.

3. Tell the user:
   > Go to **nuget.org** → click your username → **Trusted Publishing** → **Add policy**
   >
   > Enter these values:
   > - **Repository Owner**: `{owner}`
   > - **Repository**: `{repo}`
   > - **Workflow File**: `{filename}.yml`
   > - **Environment**: `{env}` *(only if the workflow uses `environment:`)*

4. Explain policy ownership: choose individual account or organization as owner. The policy applies to all packages owned by that entity.

5. Note: for **private repos**, the policy starts as "temporarily active" for 7 days. It becomes permanent after the first successful publish.

6. **Create a GitHub Environment** for publish secret scoping:
   > Go to **repo Settings** → **Environments** → **New environment** → name it `release`
   >
   > Then add a secret to this environment:
   > - Click **Add environment secret**
   > - **Name**: `NUGET_USER`
   > - **Value**: your nuget.org username (NOT email)
   >
   > Optional: add **Required reviewers** for an approval gate before publishing.

   Environment-scoped secrets are only available to workflows referencing that environment — preventing accidental use in CI jobs.

> ❌ **Don't guess the workflow filename** — the policy requires the exact filename (e.g., `publish.yml`), not the workflow `name:` field. Get it wrong and OIDC validation silently fails.

> ⚠️ Wait for the user to confirm they've created the policy before proceeding to Phase 5.

### Phase 5: Workflow Setup

Either modify an existing publish workflow or create a new one from scratch.

**If no publish workflow exists** (greenfield):
- Create a new `publish.yml` using the complete template from [references/publish-workflow.md](references/publish-workflow.md)
- Adapt the template: set the correct .NET version, project path, and environment name
- The template uses tag-triggered publishing (`on: push: tags: ['v*']`) — the standard pattern

**If a publish workflow already exists**:
- Modify it in place following the steps below

> ❌ **Don't delete the old API key secret** until trusted publishing is verified working. Keep it as a fallback. When the user is ready to remove it, explain that it's a one-way door (they'd need to regenerate on nuget.org) and wait for confirmation.

1. **Add OIDC permission and environment** to the publishing job:
   ```yaml
   jobs:
     publish:
       environment: release  # Uses the environment with NUGET_USER secret
       permissions:
         id-token: write  # Required for NuGet trusted publishing
   ```

   > ❌ **Forgetting `id-token: write`** is the most common mistake. Without it, the OIDC token request fails and `NuGet/login` will error with 403.

2. **Add the NuGet login step** before the push step:
   ```yaml
   - name: NuGet login (OIDC)
     id: login
     uses: NuGet/login@v1
     with:
       user: ${{ secrets.NUGET_USER }}  # nuget.org profile name, NOT email
   ```

   > ❌ **Don't use an email address** for the `user` input — it must be the nuget.org profile/username. Recommend storing it as an environment secret for scoping (it's not truly sensitive but scoping prevents accidental use in CI jobs).

3. **Replace the API key reference** in the push step:
   ```yaml
   # Before:
   --api-key ${{ secrets.NUGET_API_KEY }}

   # After:
   --api-key ${{ steps.login.outputs.NUGET_API_KEY }} --skip-duplicate
   ```
   Make sure the login step has `id: login` if referencing outputs by step ID. The `--skip-duplicate` flag makes pushes idempotent — safe for re-runs.

4. **Verify**: After pushing the workflow change, ask the user to trigger a publish and confirm the package appears on nuget.org.

## Common Blockers

| Problem | Cause | Action |
|---------|-------|--------|
| `NuGet/login` fails with 403 | Missing `id-token: write` permission | Add to job permissions, re-run |
| `NuGet/login` fails with "no matching policy" | Workflow filename or repo owner doesn't match policy | Verify exact filename on nuget.org (case-insensitive) |
| Push fails with unauthorized | Package ID not owned by policy account | Verify policy owner owns the package ID on nuget.org |
| Token expired | Workflow requested token too early (>1 hour before push) | Move `NuGet/login` step closer to the push step |
| Policy shows "temporarily active" | Private repo, no publish yet | Complete first publish within 7 days |
| `already_exists` on push | Re-running a publish for same version | Add `--skip-duplicate` to `dotnet nuget push` |
| GitHub Release creation 422 | Duplicate release for same tag | Explain the conflict. Recommend removing the release step from the workflow or deleting the duplicate release — but wait for confirmation before either |
| Re-run uses wrong workflow | `gh run rerun` replays the original YAML from the tag commit | Explain the situation to the user. Recommend: remove the obstacle (e.g., delete conflicting release), then re-run. Never delete and re-tag — NuGet package IDs are permanent. Wait for user confirmation before deleting anything. |

> ⚠️ If any blocker persists after one fix attempt, **stop and ask the user** — don't loop on infrastructure issues.

## References

- **Package type details**: See [references/package-types.md](references/package-types.md) for detection logic, required properties, and minimal .csproj examples per package type.
- **Publish workflow template**: See [references/publish-workflow.md](references/publish-workflow.md) for a complete tag-triggered publish workflow ready to adapt.
- **Microsoft docs**: [NuGet Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
