---
description: >-
  Deterministic same-repository evidence collector for Test Failure Analysis.
  Downloads one configured Actions artifact, validates trusted identity and PR
  freshness, sanitizes a bounded JSON/JSONL/text evidence set, and uploads only
  that compact set for the read-only analyst.

jobs:
  collect-test-evidence:
    name: Collect normalized test evidence
    runs-on: ubuntu-latest
    timeout-minutes: 10
    permissions:
      actions: read
      contents: read
      pull-requests: read
    outputs:
      evidence-found: ${{ steps.collect.outputs.evidence-found }}
      evidence-complete: ${{ steps.collect.outputs.evidence-complete }}
      completeness-reasons: ${{ steps.collect.outputs.completeness-reasons }}
      analysis-phase: ${{ steps.collect.outputs.analysis-phase }}
      pr-number: ${{ steps.collect.outputs.pr-number }}
      head-sha: ${{ steps.collect.outputs.head-sha }}
      tested-sha: ${{ steps.collect.outputs.tested-sha }}
      build-identity: ${{ steps.collect.outputs.build-identity }}
      trusted-comment-author: ${{ steps.collect.outputs.trusted-comment-author }}
      source-run-id: ${{ steps.collect.outputs.source-run-id }}
      source-run-url: ${{ steps.collect.outputs.source-run-url }}
      summary-location: ${{ steps.collect.outputs.summary-location }}
    steps:
      - name: Validate and sanitize evidence artifact
        id: collect
        shell: bash
        env:
          GH_TOKEN: ${{ github.token }}
          GH_AW_REPOSITORY: ${{ github.repository }}
          GH_AW_EVIDENCE_RUN_ID: ${{ inputs['evidence-run-id'] }}
          GH_AW_EVIDENCE_ARTIFACT_NAME: ${{ inputs['evidence-artifact-name'] }}
          GH_AW_ANALYSIS_PHASE: ${{ inputs['analysis-phase'] }}
          GH_AW_PR_NUMBER: ${{ inputs['pr-number'] }}
          GH_AW_EXPECTED_HEAD_SHA: ${{ inputs['expected-head-sha'] }}
          GH_AW_EXPECTED_TESTED_SHA: ${{ inputs['expected-tested-sha'] }}
          GH_AW_EXPECTED_BUILD_IDENTITY: ${{ inputs['expected-build-identity'] }}
          GH_AW_TRUSTED_COMMENT_AUTHOR: ${{ inputs['trusted-comment-author'] }}
          GH_AW_EXPECTED_SOURCE_RUN_URL: ${{ inputs['source-run-url'] }}
          GH_AW_EXPECTED_SUMMARY_LOCATION: ${{ inputs['evidence-summary-location'] }}
          GH_AW_DURATION_PERCENT: ${{ inputs['duration-regression-percent'] }}
          GH_AW_DURATION_SECONDS: ${{ inputs['duration-regression-minimum-seconds'] }}
          GH_AW_DURATION_SAMPLES: ${{ inputs['duration-regression-minimum-baseline-samples'] }}
          GH_AW_STAGE_DIR: ${{ runner.temp }}/test-failure-analysis-evidence
          GH_AW_ZIP_PATH: ${{ runner.temp }}/test-failure-analysis-evidence.zip
          GH_AW_RESULT_PATH: ${{ runner.temp }}/test-failure-analysis-result.json
        run: |
          set -euo pipefail

          fail() {
            echo "::error::$*" >&2
            exit 1
          }

          [ -n "${GITHUB_OUTPUT:-}" ] || fail "GITHUB_OUTPUT is unavailable."
          printf '' >> "$GITHUB_OUTPUT" || fail "GITHUB_OUTPUT is not writable."

          python3 - <<'PY'
          import os
          import re
          import unicodedata
          from decimal import Decimal, InvalidOperation

          def fail(message):
              raise SystemExit(f"::error::{message}")

          def fullmatch(name, pattern, message):
              if re.fullmatch(pattern, os.environ[name], flags=re.ASCII) is None:
                  fail(message)

          def bounded_scalar(name, limit, allow_empty=False):
              value = os.environ[name]
              if not allow_empty and not value:
                  fail(f"{name} must not be empty.")
              if len(value) > limit:
                  fail(f"{name} exceeds {limit} characters.")
              if any(unicodedata.category(ch) == "Cc" for ch in value):
                  fail(f"{name} contains a control character.")

          fullmatch(
              "GH_AW_EVIDENCE_RUN_ID",
              r"[1-9][0-9]*",
              "Evidence run ID must be a positive integer.",
          )
          fullmatch(
              "GH_AW_PR_NUMBER",
              r"[1-9][0-9]*",
              "PR number must be a positive integer.",
          )
          fullmatch(
              "GH_AW_EVIDENCE_ARTIFACT_NAME",
              r"[A-Za-z0-9][A-Za-z0-9._-]{0,127}",
              "Artifact name contains unsupported characters or is too long.",
          )
          fullmatch(
              "GH_AW_ANALYSIS_PHASE",
              r"(preliminary|final)",
              "Analysis phase must be preliminary or final.",
          )
          fullmatch(
              "GH_AW_EXPECTED_HEAD_SHA",
              r"(?i:[0-9a-f]{40}|[0-9a-f]{64})",
              "Expected head SHA is invalid.",
          )
          fullmatch(
              "GH_AW_EXPECTED_TESTED_SHA",
              r"(?i:[0-9a-f]{40}|[0-9a-f]{64})",
              "Expected tested SHA is invalid.",
          )
          fullmatch(
              "GH_AW_EXPECTED_BUILD_IDENTITY",
              r"[A-Za-z0-9][A-Za-z0-9._:@/+ -]{0,199}",
              "Build identity contains unsupported characters or is too long.",
          )
          fullmatch(
              "GH_AW_TRUSTED_COMMENT_AUTHOR",
              r"[A-Za-z0-9](?:[A-Za-z0-9-]{0,98}[A-Za-z0-9])?(?:\[bot\])?",
              "Trusted comment author is not a valid login.",
          )
          bounded_scalar("GH_AW_EXPECTED_SOURCE_RUN_URL", 300, allow_empty=True)
          if os.environ["GH_AW_EXPECTED_SUMMARY_LOCATION"]:
              fullmatch(
                  "GH_AW_EXPECTED_SUMMARY_LOCATION",
                  r"[A-Za-z0-9][A-Za-z0-9._:@/+ -]{0,299}",
                  "Summary location contains unsupported characters or is too long.",
              )

          checks = (
              ("GH_AW_DURATION_PERCENT", Decimal("0"), Decimal("10000")),
              ("GH_AW_DURATION_SECONDS", Decimal("0"), Decimal("86400")),
              ("GH_AW_DURATION_SAMPLES", Decimal("1"), Decimal("1000000")),
          )
          for name, lower, upper in checks:
              raw = os.environ[name]
              try:
                  value = Decimal(raw)
              except InvalidOperation:
                  fail(f"{name} must be numeric.")
              if not value.is_finite() or value < lower or value > upper:
                  fail(f"{name} must be between {lower} and {upper}.")
              if name.endswith("SAMPLES") and value != value.to_integral_value():
                  fail(f"{name} must be an integer.")
          PY

          PR_JSON=$(gh api "repos/${GH_AW_REPOSITORY}/pulls/${GH_AW_PR_NUMBER}") ||
            fail "Could not read the trusted pull request."
          BEFORE_HEAD=$(printf '%s' "$PR_JSON" | jq -r '.head.sha // empty')
          BEFORE_MERGE=$(printf '%s' "$PR_JSON" | jq -r '.merge_commit_sha // empty')
          [ "$BEFORE_HEAD" = "$GH_AW_EXPECTED_HEAD_SHA" ] ||
            fail "PR head does not match expected-head-sha; refusing stale evidence."
          if [ "$GH_AW_EXPECTED_TESTED_SHA" != "$BEFORE_HEAD" ] &&
             { [ -z "$BEFORE_MERGE" ] || [ "$GH_AW_EXPECTED_TESTED_SHA" != "$BEFORE_MERGE" ]; }; then
            fail "Expected tested SHA is neither the current PR head nor merge revision."
          fi

          RUN_JSON=$(gh api "repos/${GH_AW_REPOSITORY}/actions/runs/${GH_AW_EVIDENCE_RUN_ID}") ||
            fail "Could not read the configured Actions run in this repository."
          RUN_ID=$(printf '%s' "$RUN_JSON" | jq -r '.id // empty')
          RUN_REPOSITORY=$(printf '%s' "$RUN_JSON" | jq -r '.repository.full_name // empty')
          RUN_URL=$(printf '%s' "$RUN_JSON" | jq -r '.html_url // empty')
          [ "$RUN_ID" = "$GH_AW_EVIDENCE_RUN_ID" ] ||
            fail "Actions run identity mismatch."
          [ "$RUN_REPOSITORY" = "$GH_AW_REPOSITORY" ] ||
            fail "Actions run belongs to another repository."
          [ -n "$RUN_URL" ] || fail "Actions run URL is missing."
          if [ -n "$GH_AW_EXPECTED_SOURCE_RUN_URL" ] &&
             [ "$GH_AW_EXPECTED_SOURCE_RUN_URL" != "$RUN_URL" ]; then
            fail "Configured source run URL does not match the selected Actions run."
          fi
          export GH_AW_CANONICAL_RUN_URL="$RUN_URL"

          ARTIFACTS_JSON=$(gh api --method GET \
            "repos/${GH_AW_REPOSITORY}/actions/runs/${GH_AW_EVIDENCE_RUN_ID}/artifacts" \
            -f "name=${GH_AW_EVIDENCE_ARTIFACT_NAME}" -f "per_page=100") ||
            fail "Could not list evidence artifacts."
          MATCH_COUNT=$(printf '%s' "$ARTIFACTS_JSON" | jq \
            --arg name "$GH_AW_EVIDENCE_ARTIFACT_NAME" \
            '[.artifacts[] | select(.name == $name and .expired == false)] | length')
          [ "$MATCH_COUNT" -eq 1 ] ||
            fail "Expected exactly one non-expired artifact with the configured name."
          ARTIFACT_ID=$(printf '%s' "$ARTIFACTS_JSON" | jq -r \
            --arg name "$GH_AW_EVIDENCE_ARTIFACT_NAME" \
            '.artifacts[] | select(.name == $name and .expired == false) | .id')
          ARTIFACT_SIZE=$(printf '%s' "$ARTIFACTS_JSON" | jq -r \
            --arg name "$GH_AW_EVIDENCE_ARTIFACT_NAME" \
            '.artifacts[] | select(.name == $name and .expired == false) | .size_in_bytes')
          printf '%s' "$ARTIFACT_ID" | grep -qE '^[1-9][0-9]*$' ||
            fail "Artifact ID is invalid."
          printf '%s' "$ARTIFACT_SIZE" | grep -qE '^[0-9]+$' ||
            fail "Artifact size is invalid."
          [ "$ARTIFACT_SIZE" -le 67108864 ] ||
            fail "Evidence artifact exceeds the 64 MiB compressed limit."

          rm -rf "$GH_AW_STAGE_DIR"
          rm -f "$GH_AW_ZIP_PATH" "$GH_AW_RESULT_PATH"
          mkdir -p "$GH_AW_STAGE_DIR"
          gh api "repos/${GH_AW_REPOSITORY}/actions/artifacts/${ARTIFACT_ID}/zip" \
            > "$GH_AW_ZIP_PATH" ||
            fail "Evidence artifact download failed."
          DOWNLOADED_SIZE=$(stat -c '%s' "$GH_AW_ZIP_PATH")
          [ "$DOWNLOADED_SIZE" -le 67108864 ] ||
            fail "Downloaded evidence artifact exceeds the 64 MiB limit."

          python3 - <<'PY'
          import hashlib
          import json
          import os
          import re
          import shutil
          import stat
          import unicodedata
          import zipfile
          from pathlib import Path, PurePosixPath

          zip_path = Path(os.environ["GH_AW_ZIP_PATH"])
          stage = Path(os.environ["GH_AW_STAGE_DIR"])
          result_path = Path(os.environ["GH_AW_RESULT_PATH"])
          expected = {
              "repository": os.environ["GH_AW_REPOSITORY"],
              "pr_number": int(os.environ["GH_AW_PR_NUMBER"]),
              "head_sha": os.environ["GH_AW_EXPECTED_HEAD_SHA"].lower(),
              "tested_sha": os.environ["GH_AW_EXPECTED_TESTED_SHA"].lower(),
              "build_identity": os.environ["GH_AW_EXPECTED_BUILD_IDENTITY"],
              "analysis_phase": os.environ["GH_AW_ANALYSIS_PHASE"],
              "run_id": int(os.environ["GH_AW_EVIDENCE_RUN_ID"]),
              "run_url": os.environ["GH_AW_EXPECTED_SOURCE_RUN_URL"],
              "summary_location": os.environ["GH_AW_EXPECTED_SUMMARY_LOCATION"],
          }
          canonical_run_url = os.environ["GH_AW_CANONICAL_RUN_URL"]

          def fail(message):
              raise SystemExit(f"::error::{message}")

          def bounded_text(value, name, limit, allow_empty=False):
              if not isinstance(value, str):
                  fail(f"{name} must be a string.")
              if not allow_empty and not value:
                  fail(f"{name} must not be empty.")
              if len(value) > limit:
                  fail(f"{name} exceeds {limit} characters.")
              if any(unicodedata.category(ch) == "Cc" for ch in value):
                  fail(f"{name} contains a control character.")
              return value

          def safe_path(name):
              bounded_text(name, "ZIP entry path", 240)
              if "\\" in name or name.startswith("/") or ":" in name.split("/", 1)[0]:
                  fail(f"Unsafe ZIP entry path: {name!r}.")
              if any(part in ("", ".", "..") for part in name.split("/")):
                  fail(f"Unsafe ZIP entry path: {name!r}.")
              if any(
                  re.fullmatch(
                      r"[A-Za-z0-9][A-Za-z0-9._-]{0,119}", part, flags=re.ASCII
                  )
                  is None
                  for part in name.split("/")
              ):
                  fail(f"ZIP entry path uses unsupported characters: {name!r}.")
              path = PurePosixPath(name)
              return path

          try:
              archive = zipfile.ZipFile(zip_path)
          except (OSError, zipfile.BadZipFile) as exc:
              fail(f"Evidence artifact is not a valid ZIP: {exc}.")

          with archive:
              entries = archive.infolist()
              if len(entries) > 200:
                  fail("Evidence artifact exceeds the 200-entry limit.")
              seen = set()
              by_name = {}
              for info in entries:
                  path = safe_path(info.filename.rstrip("/"))
                  folded = str(path).casefold()
                  if folded in seen:
                      fail(f"Duplicate or case-colliding ZIP path: {info.filename!r}.")
                  seen.add(folded)
                  mode = (info.external_attr >> 16) & 0o170000
                  if mode == stat.S_IFLNK:
                      fail(f"Symlink entries are forbidden: {info.filename!r}.")
                  if info.flag_bits & 0x1:
                      fail(f"Encrypted ZIP entries are forbidden: {info.filename!r}.")
                  if not info.is_dir() and mode not in (0, stat.S_IFREG):
                      fail(f"Special-file ZIP entry is forbidden: {info.filename!r}.")
                  if not info.is_dir():
                      by_name[str(path)] = info

              metadata_info = by_name.get("metadata.json")
              if metadata_info is None:
                  fail("Root metadata.json is required.")
              if metadata_info.file_size > 262144:
                  fail("metadata.json exceeds 256 KiB.")
              try:
                  metadata = json.loads(archive.read(metadata_info))
              except (UnicodeDecodeError, json.JSONDecodeError) as exc:
                  fail(f"metadata.json is not valid UTF-8 JSON: {exc}.")
              if not isinstance(metadata, dict):
                  fail("metadata.json must contain an object.")

              if metadata.get("schema_version") != "1":
                  fail("Unsupported metadata schema_version.")
              if metadata.get("repository") != expected["repository"]:
                  fail("metadata.repository does not match the current repository.")
              if metadata.get("pr_number") != expected["pr_number"]:
                  fail("metadata.pr_number does not match the trusted PR.")
              if str(metadata.get("head_sha", "")).lower() != expected["head_sha"]:
                  fail("metadata.head_sha does not match expected-head-sha.")
              if str(metadata.get("tested_sha", "")).lower() != expected["tested_sha"]:
                  fail("metadata.tested_sha does not match expected-tested-sha.")
              if metadata.get("build_identity") != expected["build_identity"]:
                  fail("metadata.build_identity does not match the trusted identity.")
              if metadata.get("analysis_phase") != expected["analysis_phase"]:
                  fail("metadata.analysis_phase does not match the requested phase.")

              source = metadata.get("source")
              if not isinstance(source, dict):
                  fail("metadata.source must be an object.")
              if source.get("run_id") != expected["run_id"]:
                  fail("metadata.source.run_id does not match the selected run.")
              run_url = bounded_text(
                  source.get("run_url"), "metadata.source.run_url", 300
              )
              trusted_run_url = expected["run_url"] or canonical_run_url
              if run_url != trusted_run_url:
                  fail("metadata.source.run_url does not match the trusted run URL.")
              summary_location = bounded_text(
                  source.get("summary_location", ""),
                  "metadata.source.summary_location",
                  300,
                  allow_empty=True,
              )
              if summary_location and not all(
                  ch.isalnum() or ch in "._:@/+ -" for ch in summary_location
              ):
                  fail(
                      "metadata.source.summary_location contains unsupported "
                      "characters."
                  )
              if expected["summary_location"] and (
                  summary_location != expected["summary_location"]
              ):
                  fail(
                      "metadata.source.summary_location does not match the "
                      "trusted location."
                  )

              completeness = metadata.get("completeness")
              if not isinstance(completeness, dict):
                  fail("metadata.completeness must be an object.")
              complete = completeness.get("complete")
              if not isinstance(complete, bool):
                  fail("metadata.completeness.complete must be boolean.")
              reasons = completeness.get("reasons", [])
              if not isinstance(reasons, list) or len(reasons) > 10:
                  fail("metadata.completeness.reasons must contain at most 10 items.")
              reasons = [
                  bounded_text(item, "completeness reason", 200) for item in reasons
              ]
              for category in (
                  "failures",
                  "retries",
                  "hangs",
                  "crashes",
                  "durations",
                  "history",
              ):
                  state = completeness.get(category, "unknown")
                  if state not in ("complete", "partial", "absent", "unknown"):
                      fail(f"Invalid completeness state for {category}.")

              files = metadata.get("files")
              if not isinstance(files, list) or len(files) > 100:
                  fail("metadata.files must contain at most 100 paths.")
              selected = []
              selected_seen = set()
              for value in files:
                  if not isinstance(value, str):
                      fail("Every metadata.files entry must be a string.")
                  path = safe_path(value)
                  normalized = str(path)
                  if normalized == "metadata.json":
                      fail("metadata.files must not include metadata.json.")
                  if normalized == ".evidence-manifest.json":
                      fail("metadata.files uses a reserved evidence path.")
                  if Path(normalized).suffix.lower() not in (".json", ".jsonl", ".txt"):
                      fail(f"Unsupported evidence file type: {normalized!r}.")
                  folded = normalized.casefold()
                  if folded in selected_seen:
                      fail(f"Duplicate evidence path: {normalized!r}.")
                  selected_seen.add(folded)
                  info = by_name.get(normalized)
                  if info is None:
                      fail(f"Listed evidence file is missing: {normalized!r}.")
                  if info.file_size > 4 * 1024 * 1024:
                      fail(f"Evidence file exceeds 4 MiB: {normalized!r}.")
                  selected.append((normalized, info))

              declared_total = metadata_info.file_size + sum(
                  info.file_size for _, info in selected
              )
              if declared_total > 16 * 1024 * 1024:
                  fail("Selected evidence exceeds the 16 MiB total limit.")

              manifest_files = []
              actual_total = 0
              structured_records = 0
              text_lines = 0
              for normalized, info in [("metadata.json", metadata_info), *selected]:
                  destination = stage.joinpath(*PurePosixPath(normalized).parts)
                  destination.parent.mkdir(parents=True, exist_ok=True)
                  digest = hashlib.sha256()
                  written = 0
                  with archive.open(info) as source_stream, destination.open("wb") as out:
                      while True:
                          chunk = source_stream.read(65536)
                          if not chunk:
                              break
                          written += len(chunk)
                          actual_total += len(chunk)
                          if written > 4 * 1024 * 1024 and normalized != "metadata.json":
                              fail(f"Expanded evidence file exceeds 4 MiB: {normalized!r}.")
                          if written > 262144 and normalized == "metadata.json":
                              fail("Expanded metadata.json exceeds 256 KiB.")
                          if actual_total > 16 * 1024 * 1024:
                              fail("Expanded selected evidence exceeds 16 MiB.")
                          digest.update(chunk)
                          out.write(chunk)
                  manifest_files.append(
                      {"path": normalized, "bytes": written, "sha256": digest.hexdigest()}
                  )
                  if normalized == "metadata.json":
                      continue
                  try:
                      text = destination.read_text(encoding="utf-8")
                  except UnicodeDecodeError as exc:
                      fail(f"Evidence file is not valid UTF-8: {normalized!r}: {exc}.")
                  if "\x00" in text:
                      fail(f"Evidence file contains a NUL byte: {normalized!r}.")
                  suffix = destination.suffix.lower()
                  if suffix == ".json":
                      try:
                          document = json.loads(text)
                      except json.JSONDecodeError as exc:
                          fail(f"Evidence JSON is invalid: {normalized!r}: {exc}.")
                      if not isinstance(document, (dict, list)):
                          fail(
                              f"Evidence JSON must be an object or array: "
                              f"{normalized!r}."
                          )
                      structured_records += (
                          len(document) if isinstance(document, list) else 1
                      )
                  elif suffix == ".jsonl":
                      for line_number, line in enumerate(text.splitlines(), start=1):
                          if not line.strip():
                              continue
                          try:
                              record = json.loads(line)
                          except json.JSONDecodeError as exc:
                              fail(
                                  f"Evidence JSONL is invalid at {normalized}:"
                                  f"{line_number}: {exc}."
                              )
                          if not isinstance(record, dict):
                              fail(
                                  f"Evidence JSONL records must be objects at "
                                  f"{normalized}:{line_number}."
                              )
                          structured_records += 1
                  else:
                      text_lines += len(text.splitlines())
                  if structured_records > 5000:
                      fail("Selected evidence exceeds 5,000 structured records.")
                  if text_lines > 10000:
                      fail("Selected normalized text exceeds 10,000 lines.")

              manifest = {
                  "schema_version": "1",
                  "artifact_run_id": expected["run_id"],
                  "artifact_name": os.environ["GH_AW_EVIDENCE_ARTIFACT_NAME"],
                  "structured_records": structured_records,
                  "text_lines": text_lines,
                  "files": manifest_files,
              }
              (stage / ".evidence-manifest.json").write_text(
                  json.dumps(manifest, indent=2) + "\n", encoding="utf-8"
              )
              result_path.write_text(
                  json.dumps(
                      {
                          "evidence_complete": complete,
                          "completeness_reasons": "; ".join(reasons) or "none",
                          "source_run_url": trusted_run_url,
                          "summary_location": summary_location,
                      }
                  ),
                  encoding="utf-8",
              )
          PY

          AFTER_PR_JSON=$(gh api "repos/${GH_AW_REPOSITORY}/pulls/${GH_AW_PR_NUMBER}") ||
            fail "Could not re-read the trusted pull request after collection."
          AFTER_HEAD=$(printf '%s' "$AFTER_PR_JSON" | jq -r '.head.sha // empty')
          AFTER_MERGE=$(printf '%s' "$AFTER_PR_JSON" | jq -r '.merge_commit_sha // empty')
          [ "$AFTER_HEAD" = "$GH_AW_EXPECTED_HEAD_SHA" ] ||
            fail "PR head changed during evidence collection; refusing stale evidence."
          if [ "$GH_AW_EXPECTED_TESTED_SHA" != "$AFTER_HEAD" ] &&
             { [ -z "$AFTER_MERGE" ] || [ "$GH_AW_EXPECTED_TESTED_SHA" != "$AFTER_MERGE" ]; }; then
            fail "Tested merge revision changed during collection; refusing stale evidence."
          fi

          EVIDENCE_COMPLETE=$(jq -r '.evidence_complete' "$GH_AW_RESULT_PATH")
          COMPLETENESS_REASONS=$(jq -r '.completeness_reasons' "$GH_AW_RESULT_PATH")
          SOURCE_RUN_URL=$(jq -r '.source_run_url' "$GH_AW_RESULT_PATH")
          SUMMARY_LOCATION=$(jq -r '.summary_location' "$GH_AW_RESULT_PATH")
          {
            echo "evidence-found=true"
            echo "evidence-complete=${EVIDENCE_COMPLETE}"
            echo "completeness-reasons=${COMPLETENESS_REASONS}"
            echo "analysis-phase=${GH_AW_ANALYSIS_PHASE}"
            echo "pr-number=${GH_AW_PR_NUMBER}"
            echo "head-sha=${GH_AW_EXPECTED_HEAD_SHA}"
            echo "tested-sha=${GH_AW_EXPECTED_TESTED_SHA}"
            echo "build-identity=${GH_AW_EXPECTED_BUILD_IDENTITY}"
            echo "trusted-comment-author=${GH_AW_TRUSTED_COMMENT_AUTHOR}"
            echo "source-run-id=${GH_AW_EVIDENCE_RUN_ID}"
            echo "source-run-url=${SOURCE_RUN_URL}"
            echo "summary-location=${SUMMARY_LOCATION}"
          } >> "$GITHUB_OUTPUT"

      - name: Upload sanitized analysis artifact
        if: steps.collect.outputs.evidence-found == 'true'
        uses: actions/upload-artifact@v7.0.1
        with:
          name: test-failure-analysis-data
          path: ${{ runner.temp }}/test-failure-analysis-evidence
          if-no-files-found: error
          retention-days: "1"
          include-hidden-files: true
---
