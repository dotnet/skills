---
description: >-
  Deterministic source inventory, GitHub eligibility resolution, and trusted
  safe-output publication for Unskip Closed Tests.

jobs:
  collect-unskip-candidates:
    name: Collect verified unskip candidates
    runs-on: ubuntu-latest
    timeout-minutes: 15
    permissions:
      contents: read
      issues: read
      pull-requests: read
    outputs:
      eligible-count: ${{ steps.collect.outputs.eligible-count }}
      source-commit: ${{ steps.collect.outputs.source-commit }}
      manifest-digest: ${{ steps.collect.outputs.manifest-digest }}
    steps:
      - name: Checkout trusted source revision
        uses: actions/checkout@v7
        with:
          ref: ${{ github.sha }}
          fetch-depth: 1
          persist-credentials: false

      - name: Set up .NET SDK
        uses: actions/setup-dotnet@v6
        with:
          dotnet-version: "8.0.x"

      - name: Restore trusted inventory tool
        working-directory: .github/workflows/unskip-closed-tests-tool
        run: dotnet restore UnskipClosedTests.Tool.csproj --locked-mode

      - name: Inventory source and resolve tracking items
        id: collect
        shell: bash
        working-directory: .github/workflows/unskip-closed-tests-tool
        env:
          GH_TOKEN: ${{ github.token }}
          EXPECTED_REPOSITORY: ${{ github.repository }}
          EXPECTED_COMMIT: ${{ github.sha }}
          RAW_INVENTORY: ${{ runner.temp }}/unskip-closed-tests-inventory.json
          RESOLVED_MANIFEST: ${{ runner.temp }}/unskip-closed-tests-manifest.json
        run: |
          set -euo pipefail

          set +e
          dotnet run --no-restore \
            --project UnskipClosedTests.Tool.csproj \
            -- inventory \
            --repo-root "$GITHUB_WORKSPACE" \
            --repository "$EXPECTED_REPOSITORY" \
            --source-commit "$EXPECTED_COMMIT" \
            --config "$GITHUB_WORKSPACE/.github/workflows/unskip-closed-tests.config.json" \
            --output "$RAW_INVENTORY"
          INVENTORY_EXIT=$?
          set -e
          if [ "$INVENTORY_EXIT" -ne 0 ] && [ "$INVENTORY_EXIT" -ne 10 ]; then
            exit "$INVENTORY_EXIT"
          fi

          set +e
          dotnet run --no-restore \
            --project UnskipClosedTests.Tool.csproj \
            -- resolve \
            --manifest "$RAW_INVENTORY" \
            --output "$RESOLVED_MANIFEST"
          RESOLVE_EXIT=$?
          set -e
          if [ "$RESOLVE_EXIT" -ne 0 ] && [ "$RESOLVE_EXIT" -ne 10 ]; then
            exit "$RESOLVE_EXIT"
          fi

          ELIGIBLE_COUNT=$(jq -r '[.candidates[] | select(.decision.eligible == true)] | length' "$RESOLVED_MANIFEST")
          MANIFEST_DIGEST=$(jq -r '.manifest_digest' "$RESOLVED_MANIFEST")
          test "$MANIFEST_DIGEST" != "null"
          cp "$RESOLVED_MANIFEST" "$GITHUB_WORKSPACE/manifest.json"
          {
            echo "eligible-count=$ELIGIBLE_COUNT"
            echo "source-commit=$EXPECTED_COMMIT"
            echo "manifest-digest=$MANIFEST_DIGEST"
          } >> "$GITHUB_OUTPUT"

      - name: Upload trusted candidate manifest
        uses: actions/upload-artifact@v7
        with:
          name: unskip-closed-tests-manifest-${{ github.run_id }}-${{ github.run_attempt }}
          path: manifest.json
          if-no-files-found: error
          retention-days: 1

safe-outputs:
  jobs:
    apply-verified-unskips:
      description: >-
        Revalidate selected source sites and tracking items, apply only trusted
        Ignore removals, require exact structured test execution evidence, and
        open at most one draft pull request.
      if: >-
        needs.agent.result == 'success' &&
        needs.detection.result == 'success' &&
        needs.detection.outputs.detection_success == 'true' &&
        contains(needs.agent.outputs.output_types, 'apply_verified_unskips')
      runs-on: ubuntu-latest
      permissions:
        contents: write
        issues: read
        pull-requests: write
      inputs:
        manifest_digest:
          description: "Exact trusted manifest digest."
          required: true
          type: string
        candidate_ids_json:
          description: "JSON array of exact candidate IDs copied from the manifest."
          required: true
          type: string
      steps:
        - name: Checkout exact analyzed revision
          uses: actions/checkout@v7
          with:
            ref: ${{ github.sha }}
            fetch-depth: 0
            persist-credentials: true

        - name: Set up .NET SDK
          uses: actions/setup-dotnet@v6
          with:
            dotnet-version: "8.0.x"

        - name: Restore trusted apply tool
          working-directory: .github/workflows/unskip-closed-tests-tool
          run: dotnet restore UnskipClosedTests.Tool.csproj --locked-mode

        - name: Download original trusted manifest
          uses: actions/download-artifact@v8.0.1
          with:
            name: unskip-closed-tests-manifest-${{ github.run_id }}-${{ github.run_attempt }}
            path: ${{ runner.temp }}/unskip-closed-tests-manifest

        - name: Revalidate, edit, and verify selected candidates
          id: apply
          shell: bash
          working-directory: .github/workflows/unskip-closed-tests-tool
          env:
            GH_TOKEN: ${{ github.token }}
            EXPECTED_REPOSITORY: ${{ github.repository }}
            EXPECTED_COMMIT: ${{ github.sha }}
            ORIGINAL_MANIFEST: ${{ runner.temp }}/unskip-closed-tests-manifest/manifest.json
            RESULT_PATH: ${{ runner.temp }}/unskip-closed-tests-result.json
            RESULT_DIRECTORY: ${{ runner.temp }}/unskip-closed-tests-results
          run: |
            set -euo pipefail
            set +e
            dotnet run --no-restore \
              --project UnskipClosedTests.Tool.csproj \
              -- apply \
              --repo-root "$GITHUB_WORKSPACE" \
              --config "$GITHUB_WORKSPACE/.github/workflows/unskip-closed-tests.config.json" \
              --manifest "$ORIGINAL_MANIFEST" \
              --agent-output "$GH_AW_AGENT_OUTPUT" \
              --output "$RESULT_PATH"
            APPLY_EXIT=$?
            rm -rf bin obj
            set -e

            if [ "$APPLY_EXIT" -eq 10 ]; then
              git diff --quiet
              echo "no-action=true" >> "$GITHUB_OUTPUT"
              exit 0
            fi
            if [ "$APPLY_EXIT" -ne 0 ]; then
              exit "$APPLY_EXIT"
            fi

            test -f "$RESULT_PATH"
            jq -e \
              '.schema_version == "1" and .has_changes == true and (.changed_paths | length > 0)' \
              "$RESULT_PATH" >/dev/null
            echo "no-action=false" >> "$GITHUB_OUTPUT"
            echo "result-path=$RESULT_PATH" >> "$GITHUB_OUTPUT"

        - name: Publish one verified draft pull request
          if: steps.apply.outputs.no-action == 'false'
          shell: bash
          env:
            GH_TOKEN: ${{ github.token }}
            EXPECTED_REPOSITORY: ${{ github.repository }}
            EXPECTED_COMMIT: ${{ github.sha }}
            RESULT_PATH: ${{ steps.apply.outputs.result-path }}
          run: |
            set -euo pipefail

            DEFAULT_BRANCH=$(gh api "repos/${EXPECTED_REPOSITORY}" --jq '.default_branch')
            CURRENT_HEAD=$(gh api "repos/${EXPECTED_REPOSITORY}/commits/${DEFAULT_BRANCH}" --jq '.sha')
            test "$CURRENT_HEAD" = "$EXPECTED_COMMIT" ||
              { echo "::notice::Default branch advanced; leaving verified changes unpublished."; exit 0; }

            EXISTING=$(gh pr list \
              --repo "$EXPECTED_REPOSITORY" \
              --state open \
              --search 'in:title "[unskip-closed-tests]"' \
              --json number \
              --jq 'length')
            test "$EXISTING" -eq 0 ||
              { echo "::notice::An unskip pull request is already open."; exit 0; }

            TITLE=$(jq -r '.pr_title' "$RESULT_PATH")
            BODY_FILE="$RUNNER_TEMP/unskip-closed-tests-pr-body.md"
            EXPECTED_PATHS="$RUNNER_TEMP/unskip-closed-tests-expected-paths.txt"
            ACTUAL_PATHS="$RUNNER_TEMP/unskip-closed-tests-actual-paths.txt"
            jq -r '.pr_body' "$RESULT_PATH" > "$BODY_FILE"
            jq -r '.changed_paths[]' "$RESULT_PATH" | sort > "$EXPECTED_PATHS"
            git diff --name-only --diff-filter=M | sort > "$ACTUAL_PATHS"
            diff -u "$EXPECTED_PATHS" "$ACTUAL_PATHS"
            while IFS= read -r path; do
                test -n "$path"
                test "${path#/}" = "$path"
                case "/$path/" in
                  *"/../"*|*"/./"*) exit 20 ;;
                esac
                case "$path" in
                  *.cs) ;;
                  *) echo "::error::Unexpected changed path: $path"; exit 20 ;;
                esac
                git add -- "$path"
            done < "$EXPECTED_PATHS"

            test -n "$(git diff --cached --name-only)"
            git config user.name "github-actions[bot]"
            git config user.email "41898282+github-actions[bot]@users.noreply.github.com"
            git commit -m "Re-enable tests with resolved tracking items"

            BRANCH="automation/unskip-closed-tests-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}"
            git push origin "HEAD:refs/heads/$BRANCH"

            CURRENT_HEAD=$(gh api "repos/${EXPECTED_REPOSITORY}/commits/${DEFAULT_BRANCH}" --jq '.sha')
            if [ "$CURRENT_HEAD" != "$EXPECTED_COMMIT" ]; then
              git push origin --delete "$BRANCH"
              echo "::notice::Default branch advanced before PR creation; removed the unpublished branch."
              exit 0
            fi
            EXISTING=$(gh pr list \
              --repo "$EXPECTED_REPOSITORY" \
              --state open \
              --search 'in:title "[unskip-closed-tests]"' \
              --json number \
              --jq 'length')
            if [ "$EXISTING" -ne 0 ]; then
              git push origin --delete "$BRANCH"
              echo "::notice::Another unskip pull request opened; removed the duplicate branch."
              exit 0
            fi

            PR_URL=$(gh pr create \
              --repo "$EXPECTED_REPOSITORY" \
              --base "$DEFAULT_BRANCH" \
              --head "$BRANCH" \
              --draft \
              --title "$TITLE" \
              --body-file "$BODY_FILE")

            LIVE=$(gh pr view "$PR_URL" \
              --repo "$EXPECTED_REPOSITORY" \
              --json body,isDraft,headRefName,baseRefName,url)
            test "$(printf '%s' "$LIVE" | jq -r '.isDraft')" = "true"
            test "$(printf '%s' "$LIVE" | jq -r '.headRefName')" = "$BRANCH"
            test "$(printf '%s' "$LIVE" | jq -r '.baseRefName')" = "$DEFAULT_BRANCH"
            printf '%s' "$LIVE" | jq -r '.body' | grep -F '<!-- unskip-closed-tests:v1;'

---
