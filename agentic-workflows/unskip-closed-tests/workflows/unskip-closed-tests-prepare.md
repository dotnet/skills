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


  publish-verified-unskips:
    name: Publish verified unskips
    needs: [agent, apply_verified_unskips]
    if: >-
      needs.apply_verified_unskips.result == 'success' &&
      contains(needs.agent.outputs.output_types, 'apply_verified_unskips')
    runs-on: ubuntu-latest
    permissions:
      actions: read
      contents: write
      pull-requests: write
    steps:
      - name: Checkout exact analyzed revision without credentials
        uses: actions/checkout@v7
        with:
          ref: ${{ github.sha }}
          fetch-depth: 0
          persist-credentials: false

      - name: Download original trusted manifest
        uses: actions/download-artifact@v8.0.1
        with:
          name: unskip-closed-tests-manifest-${{ github.run_id }}-${{ github.run_attempt }}
          path: ${{ runner.temp }}/unskip-closed-tests-manifest

      - name: Set up .NET SDK
        uses: actions/setup-dotnet@v6
        with:
          dotnet-version: "8.0.x"

      - name: Restore trusted materialization tool
        working-directory: .github/workflows/unskip-closed-tests-tool
        run: dotnet restore UnskipClosedTests.Tool.csproj --locked-mode

      - name: Download untrusted verification evidence
        uses: actions/download-artifact@v8.0.1
        with:
          name: unskip-closed-tests-verification-${{ github.run_id }}-${{ github.run_attempt }}
          path: ${{ runner.temp }}/unskip-closed-tests-verification

      - name: Derive trusted publication authorization
        shell: bash
        working-directory: .github/workflows/unskip-closed-tests-tool
        env:
          GH_TOKEN: ${{ github.token }}
          ORIGINAL_MANIFEST: ${{ runner.temp }}/unskip-closed-tests-manifest/manifest.json
          VERIFICATION_EVIDENCE: ${{ runner.temp }}/unskip-closed-tests-verification
          RESULT_PATH: ${{ runner.temp }}/unskip-closed-tests-result.json
        run: |
          set -euo pipefail
          set +e
          dotnet run --no-restore \
            --project UnskipClosedTests.Tool.csproj \
            -- authorize \
            --repo-root "$GITHUB_WORKSPACE" \
            --config "$GITHUB_WORKSPACE/.github/workflows/unskip-closed-tests.config.json" \
            --manifest "$ORIGINAL_MANIFEST" \
            --agent-output "$VERIFICATION_EVIDENCE/agent-output.json" \
            --evidence-dir "$VERIFICATION_EVIDENCE" \
            --output "$RESULT_PATH"
          AUTHORIZE_EXIT=$?
          rm -rf bin obj
          set -e
          if [ "$AUTHORIZE_EXIT" -ne 0 ] && [ "$AUTHORIZE_EXIT" -ne 10 ]; then
            exit "$AUTHORIZE_EXIT"
          fi
          test -f "$RESULT_PATH"

      - name: Publish one verified draft pull request
        shell: bash
        env:
          GH_TOKEN: ${{ github.token }}
          EXPECTED_REPOSITORY: ${{ github.repository }}
          EXPECTED_COMMIT: ${{ github.sha }}
          ORIGINAL_MANIFEST: ${{ runner.temp }}/unskip-closed-tests-manifest/manifest.json
          RESULT_PATH: ${{ runner.temp }}/unskip-closed-tests-result.json
        run: |
          set -euo pipefail

          test -f "$ORIGINAL_MANIFEST"
          test -f "$RESULT_PATH"
          jq -e \
            --arg commit "$EXPECTED_COMMIT" \
            --slurpfile manifest "$ORIGINAL_MANIFEST" \
            '
              .schema_version == "1" and
              .source_commit == $commit and
              .manifest_digest == $manifest[0].manifest_digest and
              (.has_changes | type == "boolean") and
              (
                .has_changes == false or
                (
                  (.changed_paths | length > 0) and
                  (.retained_candidates | length > 0) and
                  ([.retained_candidates[].path] | unique | sort) == (.changed_paths | sort) and
                  all(
                    .retained_candidates[];
                    . as $retained |
                    any(
                      $manifest[0].candidates[];
                      .candidate_id == $retained.candidate_id and
                      .decision.eligible == true and
                      .path == $retained.path and
                      (.owner.test_fqns | sort) == ($retained.test_fqns | sort)
                    )
                  )
                )
              )
            ' \
            "$RESULT_PATH" >/dev/null
          if [ "$(jq -r '.has_changes' "$RESULT_PATH")" != "true" ]; then
            echo "::notice::No verified candidates remained; no pull request will be opened."
            exit 0
          fi

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
          printf '%s' "$TITLE" | grep -F '[unskip-closed-tests] ' >/dev/null
          grep -F '<!-- unskip-closed-tests:v1;' "$BODY_FILE" >/dev/null
          jq -r '.changed_paths[]' "$RESULT_PATH" | sort > "$EXPECTED_PATHS"
          git diff --cached --quiet
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
          done < "$EXPECTED_PATHS"

          dotnet run --no-restore \
            --project .github/workflows/unskip-closed-tests-tool/UnskipClosedTests.Tool.csproj \
            -- materialize \
            --repo-root "$GITHUB_WORKSPACE" \
            --config "$GITHUB_WORKSPACE/.github/workflows/unskip-closed-tests.config.json" \
            --manifest "$ORIGINAL_MANIFEST" \
            --result "$RESULT_PATH"
          rm -rf \
            .github/workflows/unskip-closed-tests-tool/bin \
            .github/workflows/unskip-closed-tests-tool/obj
          git diff --name-only | sort > "$ACTUAL_PATHS"
          diff -u "$EXPECTED_PATHS" "$ACTUAL_PATHS"
          while IFS= read -r path; do
            git add -- "$path"
          done < "$EXPECTED_PATHS"
          git diff --cached --name-only | sort > "$ACTUAL_PATHS"
          diff -u "$EXPECTED_PATHS" "$ACTUAL_PATHS"
          git diff --quiet
          test -n "$(git diff --cached --name-only)"
          git config user.name "github-actions[bot]"
          git config user.email "41898282+github-actions[bot]@users.noreply.github.com"
          git commit -m "Re-enable tests with resolved tracking items"

          BRANCH="automation/unskip-closed-tests-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}"
          gh auth setup-git
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
            --json body,isDraft,headRefName,baseRefName,baseRefOid,number,url)
          if [ "$(printf '%s' "$LIVE" | jq -r '.baseRefOid')" != "$EXPECTED_COMMIT" ]; then
            PR_NUMBER=$(printf '%s' "$LIVE" | jq -r '.number')
            gh pr close "$PR_NUMBER" --repo "$EXPECTED_REPOSITORY"
            git push origin --delete "$BRANCH"
            echo "::notice::Default branch advanced during PR creation; closed the PR and removed its branch."
            exit 0
          fi
          test "$(printf '%s' "$LIVE" | jq -r '.isDraft')" = "true"
          test "$(printf '%s' "$LIVE" | jq -r '.headRefName')" = "$BRANCH"
          test "$(printf '%s' "$LIVE" | jq -r '.baseRefName')" = "$DEFAULT_BRANCH"
          printf '%s' "$LIVE" | jq -r '.body' | grep -F '<!-- unskip-closed-tests:v1;'

safe-outputs:
  jobs:
    apply-verified-unskips:
      description: >-
        Revalidate selected source sites and tracking items, apply only trusted
        Ignore removals, require exact structured test execution evidence, and
        stage a bounded publication artifact from a read-only job.
      if: >-
        needs.agent.result == 'success' &&
        needs.detection.result == 'success' &&
        needs.detection.outputs.detection_success == 'true' &&
        contains(needs.agent.outputs.output_types, 'apply_verified_unskips')
      runs-on: ubuntu-latest
      permissions:
        contents: read
        issues: read
        pull-requests: read
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
            persist-credentials: false

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
            RESULT_PATH: ${{ runner.temp }}/unskip-closed-tests-untrusted-result.json
            RESULT_DIRECTORY: ${{ runner.temp }}/unskip-closed-tests-results
            EVIDENCE_DIRECTORY: ${{ runner.temp }}/unskip-closed-tests-verification-evidence
            EXPECTED_PATHS: ${{ runner.temp }}/unskip-closed-tests-expected-paths.txt
            ACTUAL_PATHS: ${{ runner.temp }}/unskip-closed-tests-actual-paths.txt
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
              --evidence-dir "$EVIDENCE_DIRECTORY" \
              --output "$RESULT_PATH"
            APPLY_EXIT=$?
            rm -rf bin obj
            set -e

            test -f "$RESULT_PATH"
            git diff --cached --quiet
            if [ "$APPLY_EXIT" -eq 10 ]; then
              git diff --quiet
              jq -e '.schema_version == "1" and .has_changes == false' "$RESULT_PATH" >/dev/null
              mkdir -p "$EVIDENCE_DIRECTORY/final"
              cp "$GH_AW_AGENT_OUTPUT" "$EVIDENCE_DIRECTORY/agent-output.json"
              echo "no-action=true" >> "$GITHUB_OUTPUT"
              exit 0
            fi
            if [ "$APPLY_EXIT" -ne 0 ]; then
              exit "$APPLY_EXIT"
            fi

            jq -e \
              '.schema_version == "1" and .has_changes == true and (.changed_paths | length > 0)' \
              "$RESULT_PATH" >/dev/null
            jq -r '.changed_paths[]' "$RESULT_PATH" | sort > "$EXPECTED_PATHS"
            git diff --name-only | sort > "$ACTUAL_PATHS"
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
            done < "$EXPECTED_PATHS"
            echo "no-action=false" >> "$GITHUB_OUTPUT"

        - name: Upload untrusted verification evidence
          uses: actions/upload-artifact@v7
          with:
            name: unskip-closed-tests-verification-${{ github.run_id }}-${{ github.run_attempt }}
            path: ${{ runner.temp }}/unskip-closed-tests-verification-evidence
            if-no-files-found: error
            retention-days: 1




---
