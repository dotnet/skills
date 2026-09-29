#!/usr/bin/env bash

# Ultimate goal: reduce maintainer time spent diagnosing genuine .NET build
# failures by producing one evidence-backed PR summary and optional inline
# suggestions, while avoiding misleading output when no build diagnosis is
# justified.
#
# Selected grading-time effect: validated safe-output requests are available
# before the safe-output job applies them, so grade whether the run requested
# one conforming terminal outcome:
# - exactly one marked summary add_comment, optionally accompanied by review
#   comments, required diagnostic structure, and no noop; or
# - exactly one noop with a workflow-defined reason code and no comment or
#   review output.
#
# Metrics:
# - build-failure-analysis-terminal-outcome-conformance (ratio,
#   higher_is_better): 1 for either conforming terminal shape, 0 for a
#   contradictory, unjustified, or missing terminal outcome, and null when the
#   request or validated-output evidence is malformed or unavailable.
# - summary-comment-count (count): number of marked summary comments.
# - review-comment-count (count): number of inline review comments.
# - noop-count (count): number of noop requests.
# Diagnostic counts are null when the output evidence cannot be trusted.

set -euo pipefail

export LC_ALL=C

emit_metrics() {
    local conformance=$1 summary_count=$2 review_count=$3 noop_count=$4

    jq -cn \
        --argjson conformance "$conformance" \
        --argjson summaryCount "$summary_count" \
        --argjson reviewCount "$review_count" \
        --argjson noopCount "$noop_count" \
        '[
            {
                id: "build-failure-analysis-terminal-outcome-conformance",
                value: $conformance
            },
            {id: "summary-comment-count", value: $summaryCount},
            {id: "review-comment-count", value: $reviewCount},
            {id: "noop-count", value: $noopCount}
        ]'
}

request=$(cat)
if ! printf '%s\n' "$request" | jq -e '
    .schemaVersion == 1
    and (.run | type == "object")
    and (.run.id | type == "string" and test("^[1-9][0-9]*$"))
    and (.run.attempt | type == "number" and . >= 1 and floor == .)
    and (.run.repository | type == "string" and test("^[^/[:space:]]+/[^/[:space:]]+$"))
    and .run.workflow == "Build Failure Analysis"
    and (.run.ref | type == "string" and length > 0)
    and (.run.sha | type == "string" and test("^[0-9a-f]{40}$"))
    and (.run.eventName == "check_run" or .run.eventName == "workflow_dispatch")
    and (.event | type == "object")
    and (.outputs | type == "array")
    and (.config | type == "object")
' >/dev/null 2>&1; then
    emit_metrics null null null null
    exit 0
fi

if ! printf '%s\n' "$request" | jq -e '
    all(.outputs[];
        (.type == "add_comment"
            and (.body | type == "string" and length > 0))
        or
        (.type == "create_pull_request_review_comment"
            and (.path | type == "string" and length > 0)
            and (
                (.line | type == "number" and . >= 1 and floor == .)
                or
                (.line | type == "string" and test("^[1-9][0-9]*$"))
            )
            and (.body | type == "string" and length > 0))
        or
        (.type == "noop"
            and (.message | type == "string" and length > 0)))
' >/dev/null 2>&1; then
    emit_metrics null null null null
    exit 0
fi

summary_count=$(printf '%s\n' "$request" | jq '
    def substantive:
        gsub("^\\s+|\\s+$"; "")
        | . as $content
        | ($content | length >= 10)
            and ($content | test("[[:alnum:]]"));

    [.outputs[]
        | select(
            .type == "add_comment"
            and (.body | contains("<!-- build-failure-analysis -->"))
            and (
                (
                    (.body | contains("## 🔍 Build Failure Analysis"))
                    and (.body | test("(?m)^\\*\\*Summary\\*\\* — .+"))
                    and (.body | test("(?m)^### Root cause [0-9]+: .+"))
                    and (
                        (
                            .body
                            | capture(
                                "(?ms)\\*\\*Affected files / errors\\*\\*\\s*"
                                + "(?<content>.*?)\\s*"
                                + "\\*\\*Proposed fix\\*\\*"
                            ).content
                        ) as $affected
                        | ($affected | substantive)
                            and ($affected | test("(?m)^- .+"))
                    )
                    and (
                        .body
                        | capture(
                            "(?ms)\\*\\*Proposed fix\\*\\*\\s*"
                            + "(?<content>.+)$"
                        ).content
                        | substantive
                    )
                )
                or
                (
                    (.body | contains("🔍 **Build Failure Analysis**"))
                    and (.body | contains("the build failed but no binary log was produced"))
                    and (.body | contains("Azure DevOps build"))
                    and (.body | contains("GitHub Actions run"))
                )
            )
        )]
    | length
')
all_comment_count=$(printf '%s\n' "$request" | jq '
    [.outputs[] | select(.type == "add_comment")] | length
')
review_count=$(printf '%s\n' "$request" | jq '
    [.outputs[] | select(.type == "create_pull_request_review_comment")] | length
')
noop_count=$(printf '%s\n' "$request" | jq '
    [.outputs[] | select(.type == "noop")] | length
')
output_count=$(printf '%s\n' "$request" | jq '.outputs | length')

summary_shape=false
if (( summary_count == 1 \
    && all_comment_count == 1 \
    && noop_count == 0 \
    && output_count == summary_count + review_count )); then
    summary_shape=true
fi

noop_shape=$(printf '%s\n' "$request" | jq -r '
    if (.outputs | length) != 1 or .outputs[0].type != "noop" then
        false
    else
        (.outputs[0].message | gsub("^\\s+|\\s+$"; "")) as $message
        | ($message | test("^\\[build-succeeded\\] Build succeeded — no analysis required\\.$"))
            or
            ($message | test(
                "^\\[non-build-failure\\] (Build|The available binlogs) compiled cleanly"
                + ".+(pipeline failure|this is) (is )?(in )?(a )?non-build"
                + ".+out of scope( for build-failure analysis)?\\.$";
                "i"
            ))
            or
            (
                ($message | test(
                    "^\\[incomplete-binlogs\\] (no|none|zero)\\b";
                    "i"
                ) | not)
                and
                ($message | test(
                    "^\\[incomplete-binlogs\\] ("
                    + "missing (build )?legs?:\\s*\\S.+"
                    + "|.+ failed without publishing (binary )?logs\\.?"
                    + "|(?:.+ )?completeness could not be verified(?: .+)?"
                    + ")$";
                    "i"
                ))
            )
            or
            ($message | test(
                "^\\[stale-revision\\] ("
                + "(unable|could not) to read (the )?(current )?PR head SHA\\.?"
                + "|.*(PR (revision|head|merge)|merge commit)"
                + ".*(moved|changed|differs|no longer).*$"
                + ")";
                "i"
            ))
    end
')

if [[ $summary_shape == true || $noop_shape == true ]]; then
    conformance=1
else
    conformance=0
fi

emit_metrics "$conformance" "$summary_count" "$review_count" "$noop_count"
