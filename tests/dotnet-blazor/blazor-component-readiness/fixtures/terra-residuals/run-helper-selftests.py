#!/usr/bin/env python3
import argparse
import base64
import copy
import hashlib
import json
import subprocess
import sys
from pathlib import Path


def write_json(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def write_text(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(value, encoding="utf-8")


def candidate_result():
    return {
        "schema_version": 1,
        "evidence_origin": "supplied-synthetic-records",
        "signature_verification_executed_now": False,
        "package_identity": {
            "id": "Neutral.Widget",
            "version": "4.2.0-rc.2",
        },
        "decisions": [
            {
                "package_role": "assessment-target",
                "package_sha256": "f94c70f23f944128f5b98ebfd0b223eb5117b8d03bdea9d8f9fe328526210fe5",
                "status": "not tested",
                "evidence_ids": [],
                "remaining_fact": "target-bound-signature-verification",
            },
            {
                "package_role": "comparison-copy",
                "package_sha256": "6bd209c518e4fb693fb2c08e57bd653e093ca8abb37e27fdc9de0f43519188ff",
                "status": "verified",
                "evidence_ids": ["receipt-comparison-copy"],
                "remaining_fact": None,
            },
        ],
        "entry_observations": [
            {
                "entry_path": "lib/net8.0/Neutral.Widget.dll",
                "containers": [
                    {
                        "package_role": "assessment-target",
                        "package_sha256": "f94c70f23f944128f5b98ebfd0b223eb5117b8d03bdea9d8f9fe328526210fe5",
                        "entry_sha256": "cf7e8ad2e52d4c6e51b20f50592404fc6b0a4806d9ab4b5240e94a6dc5770c02",
                    },
                    {
                        "package_role": "comparison-copy",
                        "package_sha256": "6bd209c518e4fb693fb2c08e57bd653e093ca8abb37e27fdc9de0f43519188ff",
                        "entry_sha256": "cf7e8ad2e52d4c6e51b20f50592404fc6b0a4806d9ab4b5240e94a6dc5770c02",
                    },
                ],
                "relationship": "identical-entry-bytes",
            },
            {
                "entry_path": ".signature.p7s",
                "containers": [
                    {
                        "package_role": "assessment-target",
                        "package_sha256": "f94c70f23f944128f5b98ebfd0b223eb5117b8d03bdea9d8f9fe328526210fe5",
                        "entry_sha256": "a0b0a7e5ad72f118de8a49c3e0f354b68ec586a75b65abd5153c9a11ca81e13a",
                    },
                    {
                        "package_role": "comparison-copy",
                        "package_sha256": "6bd209c518e4fb693fb2c08e57bd653e093ca8abb37e27fdc9de0f43519188ff",
                        "entry_sha256": "53e784c42848c5a4bd746416950cc9d0aab20fda8a7283b3e844601fcedeabbd",
                    },
                ],
                "relationship": "different-entry-bytes",
            },
        ],
    }


def release_result():
    return {
        "schema_version": 1,
        "evidence_origin": "supplied-synthetic-records",
        "operations_executed_now": False,
        "package_sha256": "fc27c5bfccd11d3a695841e36b030aa0bca646394049a1d0308982c22a87645a",
        "claims": [
            {
                "id": "release-policy-publication",
                "status": "verified",
                "evidence_ids": ["policy-release"],
                "scope": "policy-publication-only",
                "remaining_fact": None,
            },
            {
                "id": "release-job-execution",
                "status": "verified",
                "evidence_ids": ["publish-job-204", "release-run-204"],
                "scope": "single-recorded-release-run",
                "remaining_fact": None,
            },
            {
                "id": "dependency-scan-execution",
                "status": "verified",
                "evidence_ids": ["dependency-scan-204"],
                "scope": "declared-dependency-scan-scope",
                "remaining_fact": None,
            },
            {
                "id": "incident-policy-publication",
                "status": "verified",
                "evidence_ids": ["policy-incident"],
                "scope": "incident-policy-publication-only",
                "remaining_fact": None,
            },
            {
                "id": "incident-operation-execution",
                "status": "verified",
                "evidence_ids": ["incident-exercise-204"],
                "scope": "single-recorded-incident-exercise",
                "remaining_fact": None,
            },
            {
                "id": "target-signature-authentication",
                "status": "not tested",
                "evidence_ids": [],
                "scope": None,
                "remaining_fact": "target-bound-signature-verification",
            },
        ],
        "limitations": {
            "all_release_history_established": False,
            "complete_target_authentication_established": False,
            "all_vulnerabilities_closed": False,
        },
    }


def ai_row(status, evidence_ids, missing_fact=None, rationale_code=None):
    return {
        "status": status,
        "evidence_ids": evidence_ids,
        "missing_fact": missing_fact,
        "rationale_code": rationale_code,
    }


def ai_result():
    promoted_source = "source-promotion-manifest"
    promoted = {
        "AI-01": ai_row(
            "gap", [promoted_source, "upstream-contribution-inventory"]
        ),
        "AI-02": ai_row("gap", [promoted_source, "local-contribution-check"]),
        "AI-03": ai_row(
            "owner evidence required",
            [promoted_source, "maintenance-record-inventory"],
            "release-day-and-library-change-maintenance-ownership",
        ),
        "AI-04": ai_row(
            "verified", [promoted_source, "generated-code-dependencies"]
        ),
        "AI-05": ai_row(
            "verified", [promoted_source, "generated-code-dependencies"]
        ),
        "AI-06": ai_row(
            "owner evidence required",
            [promoted_source, "source-change-context", "rai-record-inventory"],
            "responsible-ai-review-before-merge",
        ),
    }
    existing = copy.deepcopy(promoted)
    existing["AI-06"] = ai_row(
        "not applicable", [promoted_source, "source-change-context"],
        rationale_code="confirmed-existing-ai-skill",
    )
    unknown = copy.deepcopy(promoted)
    unknown["AI-06"] = ai_row(None, [promoted_source], "new-ai-skill-status")
    absent = {
        f"AI-{value:02d}": ai_row(
            "not applicable",
            ["source-absence-manifest", "absent-package-inventory", "absent-upstream-inventory"],
            rationale_code="confirmed-no-promoted-ai-deliverable",
        )
        for value in range(1, 7)
    }
    return {
        "schema_version": 1,
        "cases": [
            {
                "id": "source-promoted",
                "family_applicability": "applicable",
                "rows": promoted,
            },
            {
                "id": "source-promoted-b",
                "family_applicability": "applicable",
                "rows": existing,
            },
            {
                "id": "source-promoted-c",
                "family_applicability": "applicable",
                "rows": unknown,
            },
            {
                "id": "confirmed-absent",
                "family_applicability": "not applicable",
                "rows": absent,
            },
        ],
    }


def row_result():
    return {
        "schema_version": 1,
        "rows": [
            {
                "id": "RUNTIME-SUPPORT",
                "status": "verified",
                "evidence_ids": ["support-guide"],
                "missing_facts": [],
                "next_action": None,
            },
            {
                "id": "BROWSER-SUPPORT",
                "status": "verified",
                "evidence_ids": ["support-guide"],
                "missing_facts": [],
                "next_action": None,
            },
            {
                "id": "SUPPORT-OWNERSHIP",
                "status": "owner evidence required",
                "evidence_ids": ["public-contact-directory"],
                "missing_facts": [
                    "named-backup",
                    "named-package-owner",
                    "package-scope",
                ],
                "next_action": {
                    "operation": "request-record",
                    "record_type": "package-support-accountability",
                },
            },
            {
                "id": "DISPOSAL-BEHAVIOR",
                "status": "not tested",
                "evidence_ids": ["disposal-probe-plan"],
                "missing_facts": ["post-disposal-listener-observation"],
                "next_action": {
                    "operation": "run-probe",
                    "probe": "post-disposal-listener-check",
                },
            },
        ],
    }


def prepare_inputs(root, scratch):
    generated = scratch / "generated-inputs"
    write_json(generated / "empty.json", {"schema_version": 1})
    candidate_packages = scratch / "prepared" / "candidate-binding"
    candidate_packages.mkdir(parents=True, exist_ok=True)
    candidate_fixture = root / "candidate-binding"
    package_hashes = {}
    for name in ("target", "alternate"):
        data = base64.b64decode(
            (candidate_fixture / f"{name}.nupkg.b64").read_text(encoding="utf-8").strip(),
            validate=True,
        )
        output = candidate_packages / f"{name}.nupkg"
        output.write_bytes(data)
        package_hashes[name] = hashlib.sha256(data).hexdigest()

    candidate_valid = candidate_result()
    write_json(generated / "candidate-valid.json", candidate_valid)
    candidate_wrong_positive = copy.deepcopy(candidate_valid)
    target = candidate_wrong_positive["decisions"][0]
    target.update(
        status="verified",
        evidence_ids=["receipt-comparison-copy"],
        remaining_fact=None,
    )
    write_json(generated / "candidate-wrong-positive.json", candidate_wrong_positive)
    candidate_overcautious = copy.deepcopy(candidate_valid)
    comparison = candidate_overcautious["decisions"][1]
    comparison.update(
        status="not tested",
        evidence_ids=[],
        remaining_fact="target-bound-signature-verification",
    )
    write_json(generated / "candidate-overcautious.json", candidate_overcautious)
    candidate_wrong_entry = copy.deepcopy(candidate_valid)
    candidate_wrong_entry["entry_observations"][0]["containers"][0]["entry_sha256"] = "0" * 64
    write_json(generated / "candidate-wrong-entry.json", candidate_wrong_entry)

    release_valid = release_result()
    write_json(generated / "release-valid.json", release_valid)
    release_overcautious = copy.deepcopy(release_valid)
    release_job = next(
        item
        for item in release_overcautious["claims"]
        if item["id"] == "release-job-execution"
    )
    release_job.update(
        status="not tested",
        evidence_ids=[],
        scope=None,
        remaining_fact="release-bound-run-and-job-record",
    )
    write_json(generated / "release-overcautious.json", release_overcautious)
    release_overclaim = copy.deepcopy(release_valid)
    release_overclaim["limitations"]["all_release_history_established"] = True
    write_json(generated / "release-overclaim.json", release_overclaim)
    release_limitations_array = copy.deepcopy(release_valid)
    release_limitations_array["limitations"] = [
        {"name": name, "established": value}
        for name, value in release_valid["limitations"].items()
    ]
    write_json(generated / "release-limitations-array.json", release_limitations_array)
    release_policy_as_execution = copy.deepcopy(release_valid)
    next(
        claim for claim in release_policy_as_execution["claims"]
        if claim["id"] == "release-job-execution"
    )["evidence_ids"] = ["policy-release"]
    write_json(generated / "release-policy-as-execution.json", release_policy_as_execution)
    release_unbound = json.loads(
        (root / "release-execution" / "records.json").read_text(encoding="utf-8")
    )
    next(record for record in release_unbound["records"] if record["id"] == "publish-job-204")[
        "package_sha256"
    ] = "0" * 64
    write_json(generated / "release-unbound-fixture.json", release_unbound)

    ai_valid = ai_result()
    write_json(generated / "ai-valid.json", ai_valid)
    ai_incomplete_absence = copy.deepcopy(ai_valid)
    for decision in ai_incomplete_absence["cases"][-1]["rows"].values():
        decision["evidence_ids"] = ["source-absence-manifest"]
    write_json(generated / "ai-incomplete-absence-proof.json", ai_incomplete_absence)
    ai_always_not_applicable = copy.deepcopy(ai_valid)
    promoted = ai_always_not_applicable["cases"][0]
    promoted["family_applicability"] = "not applicable"
    for decision in promoted["rows"].values():
        decision.update(
            status="not applicable",
            evidence_ids=["source-promotion-manifest"],
            missing_fact=None,
            rationale_code="confirmed-no-promoted-ai-deliverable",
        )
    write_json(generated / "ai-always-not-applicable.json", ai_always_not_applicable)
    ai_always_applicable = copy.deepcopy(ai_valid)
    absent = ai_always_applicable["cases"][-1]
    absent["family_applicability"] = "applicable"
    for decision in absent["rows"].values():
        decision.update(
            status="not tested",
            missing_fact="unspecified-ai-evidence",
            rationale_code=None,
        )
    write_json(generated / "ai-always-applicable.json", ai_always_applicable)

    for name, case_index, status in (
        ("ai-new-skipped-review", 0, "not applicable"),
        ("ai-existing-review-demand", 1, "owner evidence required"),
        ("ai-unknown-guessed-new", 2, "owner evidence required"),
        ("ai-unknown-guessed-existing", 2, "not applicable"),
    ):
        incorrect = copy.deepcopy(ai_valid)
        incorrect["cases"][case_index]["rows"]["AI-06"]["status"] = status
        write_json(generated / f"{name}.json", incorrect)

    ai_source = json.loads(
        (root / "promoted-ai" / "source-manifest.json").read_text(encoding="utf-8")
    )
    for name, reviewed_at, status in (
        ("ai-before-merge", "2031-02-01T12:00:00Z", "verified"),
        ("ai-at-merge", "2031-02-02T12:00:00Z", "gap"),
        ("ai-after-merge", "2031-02-03T12:00:00Z", "gap"),
    ):
        timed_fixture = copy.deepcopy(ai_source)
        new_skill = timed_fixture["cases"][0]
        new_skill["change_context"]["merge_state"] = "merged"
        new_skill["change_context"]["merged_at"] = "2031-02-02T12:00:00Z"
        new_skill["rai_review"]["reviews"] = [{
            "skill_path": new_skill["source_manifest"]["consumer_ai_skills"][0]["path"],
            "state": "completed",
            "completed_at": reviewed_at,
        }]
        write_json(generated / f"{name}-fixture.json", timed_fixture)
        timed_result = copy.deepcopy(ai_valid)
        timed_result["cases"][0]["rows"]["AI-06"].update(status=status, missing_fact=None)
        write_json(generated / f"{name}.json", timed_result)

    incomplete_history = copy.deepcopy(ai_source)
    incomplete_history["cases"][0]["change_context"]["base_inventory_complete"] = False
    write_json(generated / "ai-incomplete-history-fixture.json", incomplete_history)
    unresolved = copy.deepcopy(ai_valid)
    unresolved["cases"][0]["rows"]["AI-06"] = ai_row(
        None, ["source-promotion-manifest", "source-change-context"], "new-ai-skill-status"
    )
    write_json(generated / "ai-incomplete-history.json", unresolved)
    other_skill_review = copy.deepcopy(ai_source)
    other_skill_review["cases"][0]["rai_review"]["reviews"] = [{
        "skill_path": "tools/assistant/another",
        "state": "completed",
        "completed_at": "2031-02-01T12:00:00Z",
    }]
    write_json(generated / "ai-other-skill-review-fixture.json", other_skill_review)
    missing_review_time = copy.deepcopy(other_skill_review)
    review = missing_review_time["cases"][0]["rai_review"]["reviews"][0]
    review["skill_path"] = "tools/assistant/neutral-widget"
    del review["completed_at"]
    write_json(generated / "ai-missing-review-time-fixture.json", missing_review_time)

    row_root = generated / "row-output"
    valid_decisions = row_root / "review" / "neutral-widget" / "rows" / "decisions.json"
    invalid_decisions = (
        row_root / "review" / "neutral-widget" / "rows" / "decisions-catch-all.json"
    )
    empty_decisions = row_root / "review" / "neutral-widget" / "rows" / "empty.json"
    summary = row_root / "review" / "neutral-widget" / "summary.md"
    valid_links = row_root / "delivery" / "neutral-widget" / "final-links.json"
    catch_all_links = (
        row_root / "delivery" / "neutral-widget" / "final-links-catch-all.json"
    )
    escaped_links = (
        row_root / "delivery" / "neutral-widget" / "final-links-escaped.json"
    )
    wrong_summary_links = (
        row_root / "delivery" / "neutral-widget" / "final-links-wrong-summary.json"
    )
    rows_valid = row_result()
    write_json(valid_decisions, rows_valid)
    write_json(empty_decisions, {"schema_version": 1})
    rows_catch_all = copy.deepcopy(rows_valid)
    owner = next(
        item for item in rows_catch_all["rows"] if item["id"] == "SUPPORT-OWNERSHIP"
    )
    owner["evidence_ids"] = ["support-guide"]
    write_json(invalid_decisions, rows_catch_all)
    write_text(summary, "# Synthetic row summary\n")
    write_text(row_root / "decoy.md", "# Not the requested summary\n")
    write_json(
        valid_links,
        {
            "schema_version": 1,
            "artifacts": [
                {
                    "role": "row-decisions",
                    "path": "review/neutral-widget/rows/decisions.json",
                },
                {
                    "role": "summary",
                    "path": "review/neutral-widget/summary.md",
                },
            ]
        },
    )
    write_json(
        catch_all_links,
        {
            "schema_version": 1,
            "artifacts": [
                {
                    "role": "row-decisions",
                    "path": "review/neutral-widget/rows/decisions-catch-all.json",
                },
                {
                    "role": "summary",
                    "path": "review/neutral-widget/summary.md",
                },
            ]
        },
    )
    write_json(
        escaped_links,
        {
            "schema_version": 1,
            "artifacts": [
                {
                    "role": "row-decisions",
                    "path": "review/neutral-widget/rows/decisions.json",
                },
                {
                    "role": "summary",
                    "path": "../../outside-summary.md",
                },
            ]
        },
    )
    write_json(
        wrong_summary_links,
        {
            "schema_version": 1,
            "artifacts": [
                {
                    "role": "row-decisions",
                    "path": "review/neutral-widget/rows/decisions.json",
                },
                {
                    "role": "summary",
                    "path": "decoy.md",
                },
            ],
        },
    )

    raw_rows = json.loads((root / "row-evidence" / "facts.json").read_text(encoding="utf-8"))
    partial_owner = copy.deepcopy(raw_rows)
    partial_owner["evidence"][1]["package_assignments"] = [{
        "package_id": raw_rows["package_id"], "owner": "Fixture Maintainer"
    }]
    write_json(generated / "row-partial-owner-fixture.json", partial_owner)
    partial_result = copy.deepcopy(rows_valid)
    partial_result["rows"][2]["missing_facts"] = ["named-backup", "package-scope"]
    write_json(valid_decisions.with_name("decisions-partial-owner.json"), partial_result)
    partial_links = json.loads(valid_links.read_text(encoding="utf-8"))
    partial_links["artifacts"][0]["path"] = "review/neutral-widget/rows/decisions-partial-owner.json"
    write_json(valid_links.with_name("final-links-partial-owner.json"), partial_links)
    probe_as_result = copy.deepcopy(rows_valid)
    probe_as_result["rows"][3].update(status="verified", missing_facts=[], next_action=None)
    write_json(valid_decisions.with_name("decisions-probe-as-result.json"), probe_as_result)

    write_json(
        scratch / "preparation.json",
        {
            "candidate_package_sha256": package_hashes,
            "generated_inputs": generated.as_posix(),
        },
    )
    return generated, candidate_packages, row_root


def run_case(scratch, name, argv, expect_success, marker):
    case_root = scratch / "controls" / name
    case_root.mkdir(parents=True, exist_ok=True)
    environment = {"PYTHONUTF8": "1"}
    completed = subprocess.run(
        argv,
        cwd=Path(__file__).resolve().parent,
        env=environment,
        text=True,
        capture_output=True,
        check=False,
    )
    write_json(case_root / "argv.json", argv)
    write_json(case_root / "environment.json", environment)
    write_text(case_root / "cwd.txt", f"{Path(__file__).resolve().parent}\n")
    write_text(case_root / "exit-code.txt", f"{completed.returncode}\n")
    write_text(case_root / "stdout.txt", completed.stdout)
    write_text(case_root / "stderr.txt", completed.stderr)

    if expect_success:
        passed = completed.returncode == 0 and marker in completed.stdout
    else:
        passed = completed.returncode != 0 and marker in completed.stderr
    if not passed:
        raise RuntimeError(
            f"{name} failed its control: exit={completed.returncode}, "
            f"stdout={completed.stdout!r}, stderr={completed.stderr!r}"
        )
    print(f"PASS {name} exit={completed.returncode}")
    return {
        "name": name,
        "exit_code": completed.returncode,
        "expected": "success" if expect_success else "rejection",
        "marker": marker,
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--scratch", required=True)
    args = parser.parse_args()

    root = Path(__file__).resolve().parent
    helper = root.parent / "fixture-tool.py"
    scratch = Path(args.scratch).resolve()
    scratch.mkdir(parents=True, exist_ok=True)
    generated, candidate_packages, row_root = prepare_inputs(root, scratch)
    python = Path(sys.executable).resolve().as_posix()

    candidate_fixture = (root / "candidate-binding" / "evidence.json").as_posix()
    release_fixture = (root / "release-execution" / "records.json").as_posix()
    ai_fixture = (root / "promoted-ai" / "source-manifest.json").as_posix()
    row_fixture = (root / "row-evidence" / "facts.json").as_posix()
    helper_path = helper.as_posix()
    summary_path = (row_root / "review" / "neutral-widget" / "summary.md").as_posix()

    cases = [
        (
            "unknown-subcommand",
            [
                python,
                helper_path,
                "verify-unknown",
            ],
            False,
            "invalid choice: 'verify-unknown'",
        ),
        (
            "candidate-valid",
            [
                python,
                helper_path,
                "verify-candidate-binding",
                "--fixture",
                candidate_fixture,
                "--package-root",
                candidate_packages.as_posix(),
                "--input",
                (generated / "candidate-valid.json").as_posix(),
            ],
            True,
            "VALID candidate binding target-isolated positive-control",
        ),
        (
            "candidate-wrong-positive",
            [
                python,
                helper_path,
                "verify-candidate-binding",
                "--fixture",
                candidate_fixture,
                "--package-root",
                candidate_packages.as_posix(),
                "--input",
                (generated / "candidate-wrong-positive.json").as_posix(),
            ],
            False,
            "assessment-target status must be 'not tested'",
        ),
        (
            "candidate-overcautious",
            [
                python,
                helper_path,
                "verify-candidate-binding",
                "--fixture",
                candidate_fixture,
                "--package-root",
                candidate_packages.as_posix(),
                "--input",
                (generated / "candidate-overcautious.json").as_posix(),
            ],
            False,
            "comparison-copy status must be 'verified'",
        ),
        (
            "candidate-malformed",
            [
                python,
                helper_path,
                "verify-candidate-binding",
                "--fixture",
                candidate_fixture,
                "--package-root",
                candidate_packages.as_posix(),
                "--input",
                (generated / "empty.json").as_posix(),
            ],
            False,
            "candidate result must identify supplied synthetic records",
        ),
        (
            "release-valid",
            [
                python,
                helper_path,
                "verify-release-records",
                "--fixture",
                release_fixture,
                "--input",
                (generated / "release-valid.json").as_posix(),
            ],
            True,
            "VALID release execution bounded facts and limitations",
        ),
        (
            "release-overcautious",
            [
                python,
                helper_path,
                "verify-release-records",
                "--fixture",
                release_fixture,
                "--input",
                (generated / "release-overcautious.json").as_posix(),
            ],
            False,
            "release-job-execution status must be 'verified'",
        ),
        (
            "release-overclaim",
            [
                python,
                helper_path,
                "verify-release-records",
                "--fixture",
                release_fixture,
                "--input",
                (generated / "release-overclaim.json").as_posix(),
            ],
            False,
            "all_release_history_established must remain false",
        ),
        (
            "release-malformed",
            [
                python,
                helper_path,
                "verify-release-records",
                "--fixture",
                release_fixture,
                "--input",
                (generated / "empty.json").as_posix(),
            ],
            False,
            "release result must identify supplied synthetic records",
        ),
        (
            "release-limitations-array",
            [
                python,
                helper_path,
                "verify-release-records",
                "--fixture",
                release_fixture,
                "--input",
                (generated / "release-limitations-array.json").as_posix(),
            ],
            False,
            "release result needs limitations",
        ),
        (
            "ai-valid",
            [
                python,
                helper_path,
                "verify-ai-applicability",
                "--fixture",
                ai_fixture,
                "--input",
                (generated / "ai-valid.json").as_posix(),
            ],
            True,
            "VALID AI applicability promotion newness and review timing",
        ),
        (
            "ai-always-not-applicable",
            [
                python,
                helper_path,
                "verify-ai-applicability",
                "--fixture",
                ai_fixture,
                "--input",
                (generated / "ai-always-not-applicable.json").as_posix(),
            ],
            False,
            "source-promoted family_applicability must be 'applicable'",
        ),
        (
            "ai-always-applicable",
            [
                python,
                helper_path,
                "verify-ai-applicability",
                "--fixture",
                ai_fixture,
                "--input",
                (generated / "ai-always-applicable.json").as_posix(),
            ],
            False,
            "confirmed-absent family_applicability must be 'not applicable'",
        ),
        (
            "ai-malformed",
            [
                python,
                helper_path,
                "verify-ai-applicability",
                "--fixture",
                ai_fixture,
                "--input",
                (generated / "empty.json").as_posix(),
            ],
            False,
            "AI result cases must be a JSON array",
        ),
        (
            "ai-incomplete-absence-proof",
            [
                python,
                helper_path,
                "verify-ai-applicability",
                "--fixture",
                ai_fixture,
                "--input",
                (generated / "ai-incomplete-absence-proof.json").as_posix(),
            ],
            False,
            "evidence is incorrect",
        ),
        (
            "row-mapping-valid",
            [
                python,
                helper_path,
                "verify-row-evidence-map",
                "--fixture",
                row_fixture,
                "--root",
                row_root.as_posix(),
                "--input",
                (row_root / "review" / "neutral-widget" / "rows" / "decisions.json").as_posix(),
                "--links",
                (
                    row_root
                    / "delivery"
                    / "neutral-widget"
                    / "final-links.json"
                ).as_posix(),
            ],
            True,
            "VALID row evidence mapping and nested paths",
        ),
        (
            "row-mapping-catch-all",
            [
                python,
                helper_path,
                "verify-row-evidence-map",
                "--fixture",
                row_fixture,
                "--root",
                row_root.as_posix(),
                "--input",
                (
                    row_root
                    / "review"
                    / "neutral-widget"
                    / "rows"
                    / "decisions-catch-all.json"
                ).as_posix(),
                "--links",
                (
                    row_root
                    / "delivery"
                    / "neutral-widget"
                    / "final-links-catch-all.json"
                ).as_posix(),
            ],
            False,
            "SUPPORT-OWNERSHIP evidence mapping does not match relevant facts",
        ),
        (
            "row-mapping-escaped-path",
            [
                python,
                helper_path,
                "verify-row-evidence-map",
                "--fixture",
                row_fixture,
                "--root",
                row_root.as_posix(),
                "--input",
                (row_root / "review" / "neutral-widget" / "rows" / "decisions.json").as_posix(),
                "--links",
                (
                    row_root
                    / "delivery"
                    / "neutral-widget"
                    / "final-links-escaped.json"
                ).as_posix(),
            ],
            False,
            "summary path escapes output root",
        ),
        (
            "row-mapping-malformed",
            [
                python,
                helper_path,
                "verify-row-evidence-map",
                "--fixture",
                row_fixture,
                "--root",
                row_root.as_posix(),
                "--input",
                (
                    row_root / "review" / "neutral-widget" / "rows" / "empty.json"
                ).as_posix(),
                "--links",
                (
                    row_root
                    / "delivery"
                    / "neutral-widget"
                    / "final-links.json"
                ).as_posix(),
            ],
            False,
            "result rows must be a JSON array",
        ),
        (
            "row-mapping-wrong-summary",
            [
                python,
                helper_path,
                "verify-row-evidence-map",
                "--fixture",
                row_fixture,
                "--root",
                row_root.as_posix(),
                "--input",
                (row_root / "review" / "neutral-widget" / "rows" / "decisions.json").as_posix(),
                "--links",
                (row_root / "delivery" / "neutral-widget" / "final-links-wrong-summary.json").as_posix(),
            ],
            False,
            "summary final link does not identify the requested summary artifact",
        ),
    ]

    cases.append((
        "candidate-wrong-entry",
        [python, helper_path, "verify-candidate-binding", "--fixture", candidate_fixture,
         "--package-root", candidate_packages.as_posix(),
         "--input", (generated / "candidate-wrong-entry.json").as_posix()],
        False, "lost the assessment-target entry digest",
    ))
    for name, fixture_path, result_name, success, marker in (
        ("release-policy-as-execution", release_fixture, "release-policy-as-execution", False,
         "release-job-execution evidence does not match the bounded record classes"),
        ("release-unbound-job", (generated / "release-unbound-fixture.json").as_posix(),
         "release-valid", False, "release-job-execution status must be 'not tested'"),
        ("release-unbound-job-bounded", (generated / "release-unbound-fixture.json").as_posix(),
         "release-overcautious", True, "VALID release execution bounded facts and limitations"),
    ):
        cases.append((
            name,
            [python, helper_path, "verify-release-records", "--fixture", fixture_path,
             "--input", (generated / f"{result_name}.json").as_posix()],
            success, marker,
        ))

    for name, fixture_name, result_name, success, marker in (
        ("ai-new-skipped-review", None, "ai-new-skipped-review", False, "source-promoted AI-06 status is incorrect"),
        ("ai-existing-review-demand", None, "ai-existing-review-demand", False, "source-promoted-b AI-06 status is incorrect"),
        ("ai-unknown-guessed-new", None, "ai-unknown-guessed-new", False, "source-promoted-c AI-06 status is incorrect"),
        ("ai-unknown-guessed-existing", None, "ai-unknown-guessed-existing", False, "source-promoted-c AI-06 status is incorrect"),
        ("ai-before-merge", "ai-before-merge", "ai-before-merge", True, None),
        ("ai-before-merge-overcautious", "ai-before-merge", "ai-valid", False, "source-promoted AI-06 status is incorrect"),
        ("ai-at-merge", "ai-at-merge", "ai-at-merge", True, None),
        ("ai-after-merge", "ai-after-merge", "ai-after-merge", True, None),
        ("ai-after-merge-overclaim", "ai-after-merge", "ai-before-merge", False, "source-promoted AI-06 status is incorrect"),
        ("ai-incomplete-history", "ai-incomplete-history", "ai-incomplete-history", True, None),
        ("ai-incomplete-history-guessed-new", "ai-incomplete-history", "ai-valid", False, "source-promoted AI-06 status is incorrect"),
        ("ai-other-skill-review", "ai-other-skill-review", "ai-valid", True, None),
        ("ai-missing-review-time", "ai-missing-review-time", "ai-before-merge", False, "source-promoted RAI review needs a timestamp"),
    ):
        cases.append((
            name,
            [python, helper_path, "verify-ai-applicability", "--fixture",
             (generated / f"{fixture_name}-fixture.json").as_posix() if fixture_name else ai_fixture,
             "--input", (generated / f"{result_name}.json").as_posix()],
            success, marker or "VALID AI applicability promotion newness and review timing",
        ))

    for name, fixture_path, result_name, links_name, success, marker in (
        ("row-mapping-partial-owner", (generated / "row-partial-owner-fixture.json").as_posix(),
         "decisions-partial-owner", "final-links-partial-owner", True,
         "VALID row evidence mapping and nested paths"),
        ("row-mapping-stale-owner-facts", (generated / "row-partial-owner-fixture.json").as_posix(),
         "decisions", "final-links", False, "SUPPORT-OWNERSHIP missing facts are incorrect"),
        ("row-mapping-probe-plan-as-result", row_fixture, "decisions-probe-as-result", "final-links",
         False, "DISPOSAL-BEHAVIOR status must be 'not tested'"),
    ):
        cases.append((
            name,
            [python, helper_path, "verify-row-evidence-map", "--fixture", fixture_path,
             "--root", row_root.as_posix(), "--input",
             (row_root / "review" / "neutral-widget" / "rows" / f"{result_name}.json").as_posix(),
             "--links", (row_root / "delivery" / "neutral-widget" / f"{links_name}.json").as_posix()],
            success, marker,
        ))

    for _, argv, _, _ in cases:
        if argv[2] == "verify-row-evidence-map":
            argv.extend(["--summary", summary_path])

    results = [
        run_case(scratch, name, argv, expect_success, marker)
        for name, argv, expect_success, marker in cases
    ]
    write_json(
        scratch / "summary.json",
        {
            "controls": results,
            "passed": len(results),
            "failed": 0,
        },
    )
    print(f"PASS helper controls {len(results)}/{len(results)}")


if __name__ == "__main__":
    main()
