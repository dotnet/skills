# Publish Workflow Template

Complete tag-triggered GitHub Actions workflow for publishing NuGet packages with trusted publishing. Copy and adapt to your repo.

## Template

```yaml
name: Publish to NuGet

on:
  push:
    tags:
      - 'v*'  # Triggers on version tags: v1.0.0, v1.2.3-preview.1, etc.

jobs:
  publish:
    runs-on: ubuntu-latest
    environment: release  # Uses release environment for secret scoping + protection rules
    permissions:
      id-token: write     # Required for OIDC token (NuGet trusted publishing)
      contents: read

    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'  # Adjust to your target framework

      - name: Extract version from tag
        id: version
        run: echo "VERSION=${GITHUB_REF_NAME#v}" >> $GITHUB_OUTPUT

      - name: Validate version matches project
        run: |
          PROJECT_VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' path/to/YourProject.csproj)
          if [ "$PROJECT_VERSION" != "${{ steps.version.outputs.VERSION }}" ]; then
            echo "::error::Tag version (${{ steps.version.outputs.VERSION }}) doesn't match project version ($PROJECT_VERSION)"
            exit 1
          fi

      - name: Pack
        run: dotnet pack path/to/YourProject.csproj -c Release -o ./artifacts

      - name: NuGet login (OIDC)
        id: login
        uses: NuGet/login@v1
        with:
          user: ${{ secrets.NUGET_USER }}  # nuget.org profile name (NOT email)

      - name: Push to NuGet
        run: dotnet nuget push ./artifacts/*.nupkg --api-key ${{ steps.login.outputs.NUGET_API_KEY }} --source https://api.nuget.org/v3/index.json --skip-duplicate
```

## Customization Points

| Item | What to change |
|------|---------------|
| `dotnet-version` | Match your `TargetFramework` |
| `path/to/YourProject.csproj` | Path to your packable project |
| Version extraction `sed` | Adjust for `Directory.Build.props` or .NET 10 file-based apps (`#:property Version=`) |
| `--skip-duplicate` | Keeps push idempotent — safe for re-runs and matrix builds |

## Release Process

Once the workflow is committed, the publish process is:

```bash
# 1. Bump version in .csproj (and server.json for MCP servers)
# 2. Commit
git add -A && git commit -m "Bump version to 0.1.0"
# 3. Tag and push
git tag v0.1.0
git push origin main --tags
# 4. Workflow runs automatically, publishes to nuget.org
```

## Optional: GitHub Release Step

Add after the push step if you want GitHub Releases with the `.nupkg` attached. Note: this requires changing `contents: read` to `contents: write` in the job permissions.

```yaml
      - name: Create GitHub Release
        uses: softprops/action-gh-release@a06a81a03ee405af7f2048a818ed3f03bbf83c7b # v2
        with:
          files: ./artifacts/*.nupkg
          generate_release_notes: true
```

> ⚠️ **Consider omitting this step entirely.** Creating GitHub Releases separately (manually or via `gh release create` in a different workflow) avoids 422 `already_exists` conflicts and keeps the publish workflow focused on NuGet. If any release step fails, it blocks NuGet publishing too.

> ⚠️ **Don't use `ncipollo/release-action` AND `gh release create` for the same tag** — this causes HTTP 422 `already_exists` errors.

> ⚠️ **`gh run rerun` replays the original YAML** from the tag commit, not from `main`. If the workflow fails due to a release conflict, delete the conflicting release and re-run — don't delete the tag and re-tag (NuGet package IDs are permanent).

## CI vs Publish Separation

Keep your CI workflow (build + test on PR/push) separate from the publish workflow (tag-triggered). This gives you:
- CI runs on every PR without publishing
- Publish only runs on deliberate version tags
- Different permission scopes (CI doesn't need `id-token: write`)
