# CI workflow (opt-in)

Off by default. Enabled in step 2 when the user picks "scaffold CI
workflow" / "add GitHub Actions" / equivalent.

When enabled, emits `.github/workflows/evals.yml`. The workflow:

- Runs on every PR + on push to `main`.
- Always runs Tier 1 (NLP) — no creds needed, fast smoke test.
- Promotes to Tier 2 (Quality, real judge) when the repo has secrets
  `AZURE_OPENAI_ENDPOINT` + `AZURE_TENANT_ID`.
- Promotes to Tier 3 (Foundry Safety) when the repo has secret
  `AZURE_AI_FOUNDRY_ENDPOINT`.
- Uploads `report.html` as a workflow artifact.

## Template

```yaml
name: evals

on:
  pull_request:
    branches: [main]
  push:
    branches: [main]
  workflow_dispatch:

permissions:
  id-token: write      # for OIDC -> DefaultAzureCredential
  contents: read

jobs:
  evaluate:
    runs-on: ubuntu-latest
    timeout-minutes: 20

    env:
      EVAL_REPORT_FOLDER: ${{ github.run_id }}-${{ github.run_attempt }}

    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x

      - name: Restore tools
        working-directory: {{AppName}}.Evals.Tests
        run: dotnet tool restore

      - name: Azure login (only if creds present)
        if: env.AZURE_TENANT_ID != ''
        uses: azure/login@v2
        with:
          client-id: ${{ secrets.AZURE_CLIENT_ID }}
          tenant-id: ${{ secrets.AZURE_TENANT_ID }}
          subscription-id: ${{ secrets.AZURE_SUBSCRIPTION_ID }}
        env:
          AZURE_TENANT_ID: ${{ secrets.AZURE_TENANT_ID }}

      - name: Tier selection
        id: tier
        run: |
          if [ -n "${{ secrets.AZURE_OPENAI_ENDPOINT }}" ]; then
            echo "EVAL_USE_REAL_AGENT=1" >> $GITHUB_ENV
            echo "EVAL_USE_REAL_JUDGE=1" >> $GITHUB_ENV
            echo "AZURE_OPENAI_ENDPOINT=${{ secrets.AZURE_OPENAI_ENDPOINT }}" >> $GITHUB_ENV
            echo "tier=judge" >> $GITHUB_OUTPUT
          else
            echo "tier=stub"  >> $GITHUB_OUTPUT
          fi
          if [ -n "${{ secrets.AZURE_AI_FOUNDRY_ENDPOINT }}" ]; then
            echo "EVAL_USE_FOUNDRY_SAFETY=1" >> $GITHUB_ENV
            echo "AZURE_AI_FOUNDRY_ENDPOINT=${{ secrets.AZURE_AI_FOUNDRY_ENDPOINT }}" >> $GITHUB_ENV
            echo "tier=safety" >> $GITHUB_OUTPUT
          fi

      - name: Run evals (dotnet test)
        run: dotnet test {{AppName}}.Evals.Tests/{{AppName}}.Evals.Tests.csproj --logger "trx;LogFileName=evals.trx"

      - name: Generate report (already invoked by [AssemblyCleanup], this is a safety net)
        if: always()
        working-directory: {{AppName}}.Evals.Tests
        run: |
          mkdir -p $GITHUB_WORKSPACE/.copilot/perf-reports/evals/${{ env.EVAL_REPORT_FOLDER }}
          dotnet tool run aieval report \
            --path  _store \
            --output $GITHUB_WORKSPACE/.copilot/perf-reports/evals/${{ env.EVAL_REPORT_FOLDER }}/report.html

      - name: Upload report
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: eval-report-${{ steps.tier.outputs.tier }}
          path: .copilot/perf-reports/evals/${{ env.EVAL_REPORT_FOLDER }}/report.html

      - name: Upload trx
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: trx
          path: '**/evals.trx'
```

## Optional: PR comment with summary

A second job can comment on the PR with a link to the artifact and a
one-line summary scraped from `compare.md`. Not in the default
template — too much variance across teams' bot/preference choices.

## Required secrets

| Secret | Tier unlocked | Required for OIDC? |
|--------|---------------|--------------------|
| `AZURE_TENANT_ID` | needed for any Azure auth | yes |
| `AZURE_CLIENT_ID` | OIDC federated identity | yes (if using `azure/login@v2`) |
| `AZURE_SUBSCRIPTION_ID` | scope | yes |
| `AZURE_OPENAI_ENDPOINT` | Tier 2 (judge) | no |
| `AZURE_AI_FOUNDRY_ENDPOINT` | Tier 3 (safety) | no |

Document these clearly in the chat output when the workflow is scaffolded.
