#!/usr/bin/env python3
import argparse
import base64
import hashlib
import io
import json
import os
import posixpath
import re
import shutil
import subprocess
import sys
import zipfile
from datetime import datetime, timezone
from pathlib import Path
from types import SimpleNamespace


STATUSES = [
    "verified",
    "gap",
    "owner evidence required",
    "not tested",
    "not applicable",
]


def write_json(path, value):
    text = json.dumps(value, ensure_ascii=True, separators=(",", ":"))
    for character, escaped in (
        ("<", "\\u003C"),
        (">", "\\u003E"),
        ("&", "\\u0026"),
        ("'", "\\u0027"),
        ("`", "\\u0060"),
    ):
        text = text.replace(character, escaped)
    Path(path).write_text(text, encoding="utf-8")


def invalid(message):
    print(f"INVALID {message}", file=sys.stderr)
    raise SystemExit(1)


def require(condition, message):
    if not condition:
        invalid(message)


def read_json_object(path):
    try:
        value = json.loads(Path(path).read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        invalid(f"cannot read JSON object {path}: {error}")
    require(isinstance(value, dict), f"{path} must contain a JSON object")
    return value


def index_objects(value, key, label):
    require(isinstance(value, list), f"{label} must be a JSON array")
    result = {}
    for item in value:
        require(isinstance(item, dict), f"every {label} entry must be a JSON object")
        identity = item.get(key)
        require(isinstance(identity, str) and identity, f"every {label} entry needs {key}")
        require(identity not in result, f"duplicate {label} {key} {identity}")
        result[identity] = item
    return result


def sha256_bytes(value):
    return hashlib.sha256(value).hexdigest()


def read_zip_entries(path):
    try:
        package_bytes = Path(path).read_bytes()
        with zipfile.ZipFile(path) as archive:
            names = archive.namelist()
            require(len(names) == len(set(names)), f"{path} contains duplicate archive entries")
            entries = {name: archive.read(name) for name in names}
    except (OSError, zipfile.BadZipFile, KeyError) as error:
        invalid(f"cannot inspect synthetic package {path}: {error}")
    return package_bytes, entries


def ensure_existing_path_within(root, path, label):
    root = Path(root).resolve()
    candidate = Path(path).resolve()
    try:
        candidate.relative_to(root)
    except ValueError:
        invalid(f"{label} path escapes output root: {path}")
    require(candidate.is_file(), f"{label} path does not resolve to a file: {path}")
    return candidate


def resolve_link(root, path, label):
    relative = Path(path)
    require(not relative.is_absolute(), f"{label} path must be relative: {path}")
    return ensure_existing_path_within(root, Path(root) / relative, label)


def decode(args):
    Path(args.output).write_bytes(base64.b64decode(Path(args.input).read_text().strip()))


def evidence_inputs(args):
    assessment = json.loads(Path(args.assessment).read_text(encoding="utf-8"))
    identity = assessment["identity"]
    write_json(args.identity, identity)
    if assessment["assessment_kind"] != "component":
        write_json(
            args.subject,
            {
                "assessment_kind": identity["assessment_kind"],
                "package": identity["package"],
                "input_manifest_sha256": identity["input_manifest_sha256"],
                "component_id": identity["component_id"],
            },
        )

    component = identity["component_id"]
    scope = "component-specific" if assessment["assessment_kind"] == "component" else "repository-wide"
    records = [
        {
            "claim": args.claim,
            "applicability": {"scope": scope, "component_id": component if scope == "component-specific" else None},
            "provenance": {
                "kind": args.provenance,
                "locator": args.locator,
                "method": args.method,
                "captured_at_utc": "2026-09-02T20:00:00Z",
                "content_sha256": {"algorithm": "sha256", "value": args.digest},
                "retention": "commitment-only",
            },
            "supersedes": [],
        }
    ]
    if args.second_claim:
        records.append(
            {
                "claim": args.second_claim,
                "applicability": {"scope": scope, "component_id": component if scope == "component-specific" else None},
                "provenance": {
                    "kind": args.second_provenance,
                    "locator": args.second_locator,
                    "method": "Captured the supplied owner-controlled synthetic record.",
                    "captured_at_utc": "2026-09-02T20:01:00Z",
                    "content_sha256": {"algorithm": "sha256", "value": args.second_digest},
                    "retention": "commitment-only",
                },
                "supersedes": [],
            }
        )
    write_json(args.draft, {"schema_version": 1, "records": records})


def complete(args):
    assessment = json.loads(Path(args.assessment).read_text(encoding="utf-8"))
    ledger = json.loads(Path(args.ledger).read_text(encoding="utf-8"))
    evidence_ids = [record["stable_id"] for record in ledger["records"]]
    rows = assessment["rows"]
    for row in rows:
        row.update(
            {
                "status": "not applicable",
                "observation": None,
                "evidence_ids": [],
                "owner_action": None,
                "assessment_follow_up": None,
                "not_applicable_rationale": "This bounded synthetic fixture does not exercise this requirement.",
            }
        )

    if args.profile == "mixed":
        rows[0].update(
            status="gap",
            observation="The supplied artifact bytes directly demonstrate this bounded product gap.",
            evidence_ids=[evidence_ids[0]],
            not_applicable_rationale=None,
        )
        rows[1].update(
            status="owner evidence required",
            observation="The supplied materials do not establish the retained owner-controlled release fact.",
            owner_action="Supply the exact owner-controlled record for this package version.",
            not_applicable_rationale=None,
        )
        rows[2].update(
            status="not tested",
            assessment_follow_up="Run the bounded runtime probe and retain its exact local result.",
            not_applicable_rationale=None,
        )
        if len(evidence_ids) > 1:
            rows[3].update(
                status="verified",
                observation="The supplied owner-controlled record directly establishes this synthetic fact.",
                evidence_ids=[evidence_ids[1]],
                not_applicable_rationale=None,
            )
    else:
        rows[0].update(
            status="verified",
            observation="The supplied evidence directly establishes this bounded synthetic fact.",
            evidence_ids=[evidence_ids[0]],
            not_applicable_rationale=None,
        )

    assessment["completion_state"] = "complete"
    if assessment["assessment_kind"] == "package":
        assessment["summary_groups"] = []
        for status in STATUSES:
            matching = [row for row in rows if row["status"] == status]
            if matching:
                assessment["summary_groups"].append(
                    {
                        "name": status,
                        "factual_summary": f"These package rows have the factual status '{status}'.",
                        "requirement_ids": [row["id"] for row in matching],
                        "evidence_ids": sorted(
                            {evidence for row in matching for evidence in row["evidence_ids"]}
                        ),
                    }
                )
    write_json(args.output, assessment)


def verify_revision(args):
    revision = Path(args.revision)
    assessments = list(revision.glob("*.assessment.json"))
    validations = list(revision.glob("*.validation.json"))
    reports = list(revision.glob("*.report.md"))
    assert len(assessments) == len(validations) == len(reports) == 1
    assessment = json.loads(assessments[0].read_text(encoding="utf-8"))
    manifest = json.loads(validations[0].read_text(encoding="utf-8"))
    assert assessment["assessment_kind"] == args.kind
    assert len(assessment["rows"]) == args.rows
    assert assessment["completion_state"] == "complete"
    assert manifest["assessment_kind"] == args.kind
    assert manifest["completion_state"] == "complete"
    assert not (revision.parent.parent / "decision-guidance.md").exists()
    print(f"VALID {args.kind} {args.rows}")


def verify_library(args):
    root = Path(args.root)
    pointer = json.loads(
        (root / ".readiness-index" / "current-generation.json").read_text(encoding="utf-8")
    )
    generation_json = root / pointer["json_path"]
    generation_markdown = root / pointer["markdown_path"]
    assert generation_json.read_bytes() == (root / "library-index.json").read_bytes()
    assert generation_markdown.read_bytes() == (root / "library-index.md").read_bytes()
    index = json.loads(generation_json.read_text(encoding="utf-8"))
    markdown = generation_markdown.read_text(encoding="utf-8").lower()
    assert index["generation_id"] == pointer["generation_id"]
    assert f"**generation:** `{pointer['generation_id'].lower()}`" in markdown
    packages = index["packages"]
    assert len(packages) == args.packages
    assert sum(len(package["components"]) for package in packages) == args.components
    assert "remediation priority" not in markdown
    assert "release verdict" not in markdown
    assert not (root / "decision-guidance.md").exists()
    print(f"VALID library {args.packages} {args.components}")


def assert_row_not_verified(args):
    assessment = json.loads(Path(args.assessment).read_text(encoding="utf-8"))
    row = next(row for row in assessment["rows"] if row["id"] == args.id)
    assert row["status"] != "verified"
    print(f"VALID {args.id} {row['status']}")


def verify_targeted_decisions(args):
    decisions = json.loads(Path(args.input).read_text(encoding="utf-8"))
    assert decisions == {
        "CI-07": "gap",
        "CI-08": "gap",
        "BEQ-09": "gap",
        "PERF-05": "not tested",
    }
    print("VALID targeted decisions CI-07 CI-08 BEQ-09 PERF-05")


def verify_provenance_decisions(args):
    decisions = json.loads(Path(args.input).read_text(encoding="utf-8"))
    assert decisions == {
        "PI-06": "gap",
        "PI-07": "gap",
        "PI-08": "not tested",
        "PI-09": "gap",
        "PI-10": "gap",
        "PI-11": "gap",
        "PERF-02": "not applicable",
    }
    print("VALID provenance decisions PI-06 PI-07 PI-08 PI-09 PI-10 PI-11 PERF-02")


def verify_dynamic(args):
    root = Path(args.root)
    manifest = json.loads((root / "input.confirmed.json").read_text(encoding="utf-8"))
    component = next(item for item in manifest["components"] if item["id"] == "dynamic-group")
    lifecycle = component["dynamic_child_lifecycle"]
    assert lifecycle["applicability"] == "required"
    assert lifecycle["triggers"]
    inputs = {item["basename"]: item for item in manifest["evidence_inputs"]}
    raw = [item for item in inputs.values() if item["kind"] == "raw-observation"]
    assert len(raw) == 10
    protocol_input = next(
        item for item in inputs.values() if item["kind"] == "structured-protocol"
    )
    protocol_path = root / protocol_input["basename"]
    protocol_bytes = protocol_path.read_bytes()
    assert hashlib.sha256(protocol_bytes).hexdigest() == protocol_input["content_sha256"]["value"]
    protocol = json.loads(protocol_bytes)
    operations = protocol["operations"]
    expected = [
        "add-child",
        "remove-child",
        "keyed-reorder",
        "disable-or-remove-selected-child",
        "membership-reconciliation",
        "propagated-name-default-value-state",
        "focus-ownership-restoration",
        "single-roving-tab-stop",
        "callbacks-error-routing",
        "cleanup-disposal",
    ]
    assert [item["operation"] for item in operations] == expected
    for item in operations:
        assert item["disposition"] in ("observed", "not-tested")
        if item["disposition"] == "observed":
            assert item["outcome"] in ("passed", "failed")
            digest = item["raw_observation_sha256"]["value"]
            raw_input = next(value for value in raw if value["content_sha256"]["value"] == digest)
            raw_bytes = (root / raw_input["basename"]).read_bytes()
            assert hashlib.sha256(raw_bytes).hexdigest() == digest
            observation = json.loads(raw_bytes)
            assert observation["operation"] == item["operation"]
            assert observation["phase"] == "after-initial-render"
        else:
            assert item["not_tested_reason"]

    revision = root / "revisions" / "0001"
    assessment = json.loads((revision / "unified.assessment.json").read_text(encoding="utf-8"))
    evidence = json.loads((revision / "unified.evidence.json").read_text(encoding="utf-8"))
    record = next(
        record
        for ledger in evidence["source_ledgers"]
        for record in ledger["ledger"]["records"]
        if record["provenance"]["method"] == "protocol:dynamic-child-lifecycle-v1"
    )
    for row_id in ("A11Y-06", "A11Y-07", "A11Y-08", "BEQ-12", "BEQ-15"):
        row = next(row for row in assessment["rows"] if row["id"] == row_id)
        assert row["status"] in ("verified", "gap", "not tested")
        assert record["stable_id"] in row["evidence_ids"]
    verify = argparse.Namespace(revision=str(revision), kind="unified", rows=121)
    verify_revision(verify)
    print("VALID dynamic lifecycle 10")


def verify_blind_gate(args):
    root = Path(args.root)
    fixture = Path(args.fixture)
    draft_path = fixture / "comparison-inputs.draft.json"
    draft = json.loads(draft_path.read_text(encoding="utf-8"))
    missing_paths = [
        item["path"]
        for item in draft["allowed_inputs"]
        if not (fixture / item["path"]).is_file()
    ]
    assert not missing_paths
    coverage_ids = {item["id"] for item in draft["coverage"]}
    missing_surface = "browser-interop-and-style-assets"
    assert missing_surface not in coverage_ids

    preflight = root / "preflight"
    command = (preflight / "command.txt").read_text(encoding="utf-8").strip()
    exit_code = int((preflight / "exit-code.txt").read_text(encoding="utf-8").strip())
    stdout = (preflight / "stdout.txt").read_text(encoding="utf-8")
    stderr = (preflight / "stderr.txt").read_text(encoding="utf-8")
    assert "comparison inputs-freeze" in command or "comparison inputs-validate" in command
    assert "comparison-inputs.draft.json" in command
    assert exit_code != 0
    assert "validation error:" in stderr.lower()
    assert missing_surface in stdout + stderr
    assert not (root / "comparison-inputs.confirmed.json").exists()
    assert not (root / "worker-handoff.json").exists()
    assert not (root / "revisions").exists()
    print(f"VALID blind gate rejected missing coverage surface {missing_surface}")


def verify_candidate_binding(args):
    fixture = read_json_object(args.fixture)
    result = read_json_object(args.input)
    require(fixture.get("schema_version") == 1, "candidate fixture schema version is unsupported")
    require(result.get("schema_version") == 1, "candidate result schema version is unsupported")
    require(
        fixture.get("fixture_kind") == "synthetic-static-signature-binding",
        "candidate fixture kind is unsupported",
    )
    require(
        result.get("evidence_origin") == fixture.get("record_origin"),
        "candidate result must identify supplied synthetic records",
    )
    require(
        result.get("signature_verification_executed_now") is False,
        "candidate result must not claim an executed signature verification",
    )

    identity = fixture.get("package_identity")
    require(isinstance(identity, dict), "candidate fixture needs package_identity")
    package_id = identity.get("id")
    version = identity.get("version")
    require(isinstance(package_id, str) and package_id, "candidate fixture needs package ID")
    require(isinstance(version, str) and version, "candidate fixture needs package version")
    require(
        result.get("package_identity") == identity,
        "candidate result lost the shared package ID or version",
    )

    package_records = index_objects(fixture.get("packages"), "role", "candidate packages")
    selected_roles = [
        role for role, package in package_records.items() if package.get("selected_target") is True
    ]
    require(len(selected_roles) == 1, "candidate fixture must declare one selected target")
    selected_role = selected_roles[0]

    entry_paths = fixture.get("observed_entry_paths")
    require(isinstance(entry_paths, list) and entry_paths, "candidate fixture needs observed entries")
    require(
        len(entry_paths) == len(set(entry_paths)),
        "candidate fixture contains duplicate observed entry paths",
    )
    signature_entry_path = fixture.get("signature_entry_path")
    require(
        isinstance(signature_entry_path, str) and signature_entry_path in entry_paths,
        "candidate fixture must observe its signature entry",
    )
    corresponding_entry_path = fixture.get("corresponding_entry_path")
    require(
        isinstance(corresponding_entry_path, str) and corresponding_entry_path in entry_paths,
        "candidate fixture must identify its corresponding payload entry",
    )
    package_root = Path(args.package_root)
    inspected = {}
    for role, package in package_records.items():
        file_name = package.get("file")
        require(isinstance(file_name, str) and file_name, f"{role} needs a package file")
        package_bytes, entries = read_zip_entries(package_root / file_name)
        package_sha256 = sha256_bytes(package_bytes)
        require(
            package_sha256 == package.get("package_sha256"),
            f"{role} whole-package digest does not match the fixture",
        )
        for entry_path in entry_paths:
            require(entry_path in entries, f"{role} is missing archive entry {entry_path}")
        nuspecs = [name for name in entries if name.endswith(".nuspec")]
        require(len(nuspecs) == 1, f"{role} must contain one nuspec")
        nuspec = entries[nuspecs[0]].decode("utf-8")
        require(f"<id>{package_id}</id>" in nuspec, f"{role} package ID does not match")
        require(f"<version>{version}</version>" in nuspec, f"{role} version does not match")
        require(
            any(
                name.endswith("SYNTHETIC-TEST-ONLY.txt")
                and b"inert synthetic test data" in value
                for name, value in entries.items()
            ),
            f"{role} lacks the synthetic-test marker",
        )
        inspected[role] = {
            "package_sha256": package_sha256,
            "entries": {path: sha256_bytes(entries[path]) for path in entry_paths},
        }

    require(
        len({item["package_sha256"] for item in inspected.values()}) == len(inspected),
        "candidate fixture whole-package digests must be distinct",
    )
    require(
        len(
            {
                item["entries"][corresponding_entry_path]
                for item in inspected.values()
            }
        )
        == 1,
        "candidate fixture payload entry bytes must be identical",
    )
    require(
        len({item["entries"][signature_entry_path] for item in inspected.values()})
        == len(inspected),
        "candidate fixture signature entry bytes must be distinct",
    )

    observations = index_objects(
        fixture.get("signature_observations"), "id", "signature observations"
    )
    for observation_id, observation in observations.items():
        require(
            observation.get("synthetic_test_data") is True
            and observation.get("executed_now") is False,
            f"{observation_id} must be an unexecuted synthetic record",
        )

    decisions = index_objects(result.get("decisions"), "package_role", "candidate decisions")
    require(
        set(decisions) == set(package_records),
        "candidate decisions must cover every declared package role exactly once",
    )
    expected_statuses = {}
    for role, inspected_package in inspected.items():
        matching_observations = sorted(
            observation_id
            for observation_id, observation in observations.items()
            if observation.get("result") == "valid"
            and observation.get("package_sha256") == inspected_package["package_sha256"]
            and observation.get("signature_entry_path") == signature_entry_path
            and observation.get("signature_entry_sha256")
            == inspected_package["entries"][signature_entry_path]
        )
        expected_status = "verified" if matching_observations else "not tested"
        expected_statuses[role] = expected_status
        decision = decisions[role]
        require(
            decision.get("package_sha256") == inspected_package["package_sha256"],
            f"{role} decision lost the containing package identity",
        )
        require(
            decision.get("status") == expected_status,
            f"{role} status must be '{expected_status}'",
        )
        require(
            sorted(decision.get("evidence_ids") or []) == matching_observations,
            f"{role} signature evidence is not bound to that package",
        )
        expected_remaining_fact = (
            None if matching_observations else "target-bound-signature-verification"
        )
        require(
            decision.get("remaining_fact") == expected_remaining_fact,
            f"{role} remaining signature fact is incorrect",
        )

    require(
        expected_statuses[selected_role] == "not tested",
        "candidate fixture must leave the selected target without target-bound verification",
    )
    require(
        any(
            role != selected_role and status == "verified"
            for role, status in expected_statuses.items()
        ),
        "candidate fixture must include a supported positive control",
    )

    entry_observations = index_objects(
        result.get("entry_observations"), "entry_path", "entry observations"
    )
    require(
        set(entry_observations) == set(entry_paths),
        "entry observations must cover every declared entry path",
    )
    for entry_path in entry_paths:
        observation = entry_observations[entry_path]
        containers = index_objects(
            observation.get("containers"), "package_role", f"{entry_path} containers"
        )
        require(
            set(containers) == set(inspected),
            f"{entry_path} must retain every containing package identity",
        )
        entry_digests = set()
        for role, inspected_package in inspected.items():
            container = containers[role]
            require(
                container.get("package_sha256") == inspected_package["package_sha256"],
                f"{entry_path} lost the {role} package digest",
            )
            require(
                container.get("entry_sha256") == inspected_package["entries"][entry_path],
                f"{entry_path} lost the {role} entry digest",
            )
            entry_digests.add(container["entry_sha256"])
        expected_relationship = (
            "identical-entry-bytes" if len(entry_digests) == 1 else "different-entry-bytes"
        )
        require(
            observation.get("relationship") == expected_relationship,
            f"{entry_path} relationship must be '{expected_relationship}'",
        )

    print("VALID candidate binding target-isolated positive-control")


def verify_release_records(args):
    fixture = read_json_object(args.fixture)
    result = read_json_object(args.input)
    require(fixture.get("schema_version") == 1, "release fixture schema version is unsupported")
    require(result.get("schema_version") == 1, "release result schema version is unsupported")
    require(
        fixture.get("fixture_kind") == "synthetic-release-execution-records",
        "release fixture kind is unsupported",
    )
    require(
        result.get("evidence_origin") == fixture.get("record_origin"),
        "release result must identify supplied synthetic records",
    )
    require(
        result.get("operations_executed_now") is False,
        "release result must not claim operations were executed during assessment",
    )

    package = fixture.get("package")
    require(isinstance(package, dict), "release fixture needs a package identity")
    package_sha256 = package.get("sha256")
    require(isinstance(package_sha256, str) and len(package_sha256) == 64, "invalid package digest")
    require(
        result.get("package_sha256") == package_sha256,
        "release result lost the target package digest",
    )
    claims = index_objects(fixture.get("claims"), "id", "release claims")
    records = index_objects(fixture.get("records"), "id", "release records")
    decisions = index_objects(result.get("claims"), "id", "release decisions")
    require(
        set(decisions) == set(claims),
        "release decisions must cover every declared claim exactly once",
    )

    # These are the exercise's six questions, not a protocol for disclosure rubric rows.
    contracts = {
        "release-policy-publication": (
            {"policy-publication"}, "published", False, "policy-publication-only", None
        ),
        "release-job-execution": (
            {"release-run", "release-job"}, "occurred", True,
            "single-recorded-release-run", "release-bound-run-and-job-record"
        ),
        "dependency-scan-execution": (
            {"scan-run"}, "occurred", True,
            "declared-dependency-scan-scope", "release-bound-scan-record"
        ),
        "incident-policy-publication": (
            {"incident-policy-publication"}, "published", False,
            "incident-policy-publication-only", None
        ),
        "incident-operation-execution": (
            {"incident-operation"}, "occurred", True,
            "single-recorded-incident-exercise", "release-bound-incident-operation-record"
        ),
        "target-signature-authentication": (
            {"target-signature-verification"}, "occurred", True,
            "target-signature-verification", "target-bound-signature-verification"
        ),
    }
    require(set(claims) == set(contracts), "release fixture questions are unsupported")
    for claim_id, contract in contracts.items():
        required_classes, fact, package_bound, scope, remaining = contract
        matching = []
        for record_id, record in records.items():
            if record.get("record_class") not in required_classes:
                continue
            if package_bound and record.get("package_sha256") != package_sha256:
                continue
            if record.get(fact) is not True:
                continue
            matching.append(record_id)

        present_classes = {records[record_id]["record_class"] for record_id in matching}
        established = required_classes.issubset(present_classes)
        expected_status = "verified" if established else "not tested"
        expected_evidence = sorted(matching)
        decision = decisions[claim_id]
        require(
            decision.get("status") == expected_status,
            f"{claim_id} status must be '{expected_status}'",
        )
        require(
            sorted(decision.get("evidence_ids") or []) == expected_evidence,
            f"{claim_id} evidence does not match the bounded record classes",
        )
        require(
            decision.get("scope") == (scope if matching else None),
            f"{claim_id} bounded scope is incorrect",
        )
        require(
            decision.get("remaining_fact")
            == (None if established else remaining),
            f"{claim_id} remaining fact is incorrect",
        )

    expected_limits = {
        "all_release_history_established": False,
        "complete_target_authentication_established": False,
        "all_vulnerabilities_closed": False,
    }
    actual_limits = result.get("limitations")
    require(isinstance(actual_limits, dict), "release result needs limitations")
    require(
        set(actual_limits) == set(expected_limits),
        "release result must contain exactly the three coverage limitations",
    )
    for name, expected in expected_limits.items():
        require(
            actual_limits.get(name) is expected,
            f"{name} must remain {str(expected).lower()}",
        )

    print("VALID release execution bounded facts and limitations")


def verify_ai_applicability(args):
    fixture = read_json_object(args.fixture)
    result = read_json_object(args.input)
    require(fixture.get("schema_version") == 1, "AI fixture schema version is unsupported")
    require(result.get("schema_version") == 1, "AI result schema version is unsupported")
    require(
        fixture.get("fixture_kind") == "synthetic-consumer-ai-deliverables",
        "AI fixture kind is unsupported",
    )
    fixture_cases = index_objects(fixture.get("cases"), "id", "AI fixture cases")
    result_cases = index_objects(result.get("cases"), "id", "AI result cases")
    require(
        set(result_cases) == set(fixture_cases),
        "AI results must cover every fixture case exactly once",
    )
    expected_ai_ids = {f"AI-{value:02d}" for value in range(1, 7)}
    saw_applicable = False
    saw_not_applicable = False

    for case_id, case in fixture_cases.items():
        source = case.get("source_manifest")
        require(isinstance(source, dict), f"{case_id} needs a source manifest")
        source_evidence = source.get("evidence_id")
        skills = source.get("consumer_ai_skills")
        require(isinstance(skills, list), f"{case_id} source skills must be an array")
        nupkg = case.get("nupkg_inventory")
        require(isinstance(nupkg, dict), f"{case_id} needs a package inventory")
        package_entries = nupkg.get("ai_skill_entries")
        require(
            nupkg.get("inventory_complete") is True and isinstance(package_entries, list),
            f"{case_id} package inventory must be complete",
        )
        upstream = case.get("upstream_inventory")
        require(isinstance(upstream, dict), f"{case_id} needs an upstream inventory")
        contributions = upstream.get("contributions")
        require(
            upstream.get("inventory_complete") is True and isinstance(contributions, list),
            f"{case_id} upstream inventory must be complete",
        )
        promoted_skills = [
            skill for skill in skills if isinstance(skill, dict) and skill.get("promoted") is True
        ]
        explicit_absence = (
            source.get("inventory_complete") is True
            and source.get("promotion_claim") == "not-promoted"
            and not skills
        )
        case_result = result_cases[case_id]
        rows = case_result.get("rows")
        require(isinstance(rows, dict), f"{case_id} result needs rows")
        require(set(rows) == expected_ai_ids, f"{case_id} must report all six AI rows")

        expected = {}
        if promoted_skills:
            saw_applicable = True
            expected_applicability = "applicable"
            promoted_names = {skill.get("name") for skill in promoted_skills}
            require(
                not package_entries,
                f"{case_id} must exercise a promoted source skill absent from the package",
            )
            contributed = any(
                isinstance(item, dict) and item.get("name") in promoted_names
                for item in contributions
            )
            expected["AI-01"] = {
                "status": "verified" if contributed else "gap",
                "evidence_ids": [source_evidence, upstream.get("evidence_id")],
                "missing_fact": None,
                "rationale_code": None,
            }

            guidance = case.get("contribution_guidance_check")
            require(isinstance(guidance, dict), f"{case_id} needs a guidance check")
            guidance_passed = all(
                guidance.get(name) is True for name in ("format", "evals", "ownership")
            )
            expected["AI-02"] = {
                "status": "verified" if guidance_passed else "gap",
                "evidence_ids": [source_evidence, guidance.get("evidence_id")],
                "missing_fact": None,
                "rationale_code": None,
            }

            maintenance = case.get("maintenance_record")
            require(isinstance(maintenance, dict), f"{case_id} needs maintenance evidence")
            assignments = maintenance.get("assignments")
            require(isinstance(assignments, list), f"{case_id} assignments must be an array")
            maintenance_available = any(
                isinstance(assignment, dict) and assignment.get("owner")
                and {"framework-release-day", "library-change"}.issubset(
                    set(assignment.get("responsibilities") or [])
                )
                for assignment in assignments
            )
            expected["AI-03"] = {
                "status": "verified" if maintenance_available else "owner evidence required",
                "evidence_ids": [source_evidence, maintenance.get("evidence_id")],
                "missing_fact": (
                    None if maintenance_available else
                    "release-day-and-library-change-maintenance-ownership"
                ),
                "rationale_code": None,
            }

            dependencies = case.get("dependency_inventory")
            require(isinstance(dependencies, dict), f"{case_id} needs dependency evidence")
            dependency_items = dependencies.get("items")
            require(
                dependencies.get("inventory_complete") is True
                and isinstance(dependency_items, list),
                f"{case_id} dependency inventory must be complete",
            )
            allowed_dependency_surface = all(
                isinstance(item, dict)
                and item.get("ecosystem") in ("dotnet", "qualifying-partner")
                for item in dependency_items
            )
            no_closed_dependency = all(
                item.get("proprietary") is False and item.get("closed_source") is False
                for item in dependency_items
            )
            expected["AI-04"] = {
                "status": "verified" if allowed_dependency_surface else "gap",
                "evidence_ids": [source_evidence, dependencies.get("evidence_id")],
                "missing_fact": None,
                "rationale_code": None,
            }
            expected["AI-05"] = {
                "status": "verified" if no_closed_dependency else "gap",
                "evidence_ids": [source_evidence, dependencies.get("evidence_id")],
                "missing_fact": None,
                "rationale_code": None,
            }

            require(len(promoted_skills) == 1, f"{case_id} must isolate one promoted skill")
            skill_path = promoted_skills[0].get("path")
            require(isinstance(skill_path, str) and skill_path, f"{case_id} needs a skill path")
            context = case.get("change_context")
            new_skill = None
            context_evidence = [source_evidence]
            if context is not None:
                require(isinstance(context, dict), f"{case_id} change context must be an object")
                base_paths = context.get("base_skill_paths")
                require(
                    isinstance(base_paths, list)
                    and all(isinstance(path, str) for path in base_paths)
                    and isinstance(context.get("base_inventory_complete"), bool),
                    f"{case_id} needs the base inventory and its completeness",
                )
                context_evidence.append(context.get("evidence_id"))
                if skill_path in base_paths:
                    new_skill = False
                elif context["base_inventory_complete"]:
                    new_skill = True

            if new_skill is None:
                expected["AI-06"] = {
                    "status": None,
                    "evidence_ids": context_evidence,
                    "missing_fact": "new-ai-skill-status",
                    "rationale_code": None,
                }
            elif not new_skill:
                expected["AI-06"] = {
                    "status": "not applicable",
                    "evidence_ids": context_evidence,
                    "missing_fact": None,
                    "rationale_code": "confirmed-existing-ai-skill",
                }
            else:
                rai = case.get("rai_review")
                require(isinstance(rai, dict), f"{case_id} needs a RAI record inventory")
                reviews = rai.get("reviews")
                require(
                    isinstance(reviews, list) and all(isinstance(review, dict) for review in reviews),
                    f"{case_id} RAI reviews must be an object array",
                )
                merged_at = context.get("merged_at")
                merge_state = context.get("merge_state")
                require(merge_state in ("open", "merged"), f"{case_id} needs the merge state")
                require(merge_state != "open" or merged_at is None, f"{case_id} open change has a merge date")
                merge_time = (
                    read_utc_time(merged_at, f"{case_id} merge") if merge_state == "merged" else None
                )
                review_times = [
                    read_utc_time(review.get("completed_at"), f"{case_id} RAI review")
                    for review in reviews
                    if review.get("skill_path") == skill_path and review.get("state") == "completed"
                ]
                on_time = any(merge_time is None or reviewed < merge_time for reviewed in review_times)
                rai_status = (
                    "verified" if on_time else "gap" if review_times else "owner evidence required"
                )
                expected["AI-06"] = {
                    "status": rai_status,
                    "evidence_ids": context_evidence + [rai.get("evidence_id")],
                    "missing_fact": (
                        "responsible-ai-review-before-merge" if not review_times else None
                    ),
                    "rationale_code": None,
                }
        elif explicit_absence:
            saw_not_applicable = True
            require(
                not package_entries and not contributions,
                f"{case_id} absent control must have complete empty package and upstream inventories",
            )
            expected_applicability = "not applicable"
            for row_id in expected_ai_ids:
                expected[row_id] = {
                    "status": "not applicable",
                    "evidence_ids": [source_evidence, nupkg.get("evidence_id"), upstream.get("evidence_id")],
                    "missing_fact": None,
                    "rationale_code": "confirmed-no-promoted-ai-deliverable",
                }
        else:
            invalid(f"{case_id} AI applicability is unresolved by its source manifest")

        require(
            case_result.get("family_applicability") == expected_applicability,
            f"{case_id} family_applicability must be '{expected_applicability}'",
        )
        for row_id, expected_decision in expected.items():
            decision = rows[row_id]
            require(isinstance(decision, dict), f"{case_id} {row_id} must be an object")
            for field in ("status", "missing_fact", "rationale_code"):
                require(
                    decision.get(field) == expected_decision[field],
                    f"{case_id} {row_id} {field} is incorrect",
                )
            require(
                sorted(decision.get("evidence_ids") or [])
                == sorted(expected_decision["evidence_ids"]),
                f"{case_id} {row_id} evidence is incorrect",
            )

    require(saw_applicable, "AI fixture must include an applicable promoted-source case")
    require(saw_not_applicable, "AI fixture must include a confirmed absent control")
    print("VALID AI applicability promotion newness and review timing")


def read_utc_time(value, label):
    require(isinstance(value, str), f"{label} needs a timestamp")
    try:
        timestamp = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        invalid(f"{label} timestamp is invalid")
    require(timestamp.tzinfo == timezone.utc, f"{label} timestamp must be UTC")
    return timestamp


def verify_row_evidence_map(args):
    fixture = read_json_object(args.fixture)
    result = read_json_object(args.input)
    links = read_json_object(args.links)
    require(fixture.get("schema_version") == 1, "row fixture schema version is unsupported")
    require(result.get("schema_version") == 1, "row result schema version is unsupported")
    require(links.get("schema_version") == 1, "final links schema version is unsupported")
    require(
        fixture.get("fixture_kind") == "synthetic-row-evidence-map",
        "row-mapping fixture kind is unsupported",
    )
    root = Path(args.root).resolve()
    result_path = ensure_existing_path_within(root, args.input, "row decisions")
    summary_path = ensure_existing_path_within(root, args.summary, "requested summary")
    require(
        summary_path != result_path and summary_path.suffix == ".md",
        "requested summary must identify a distinct Markdown artifact",
    )
    ensure_existing_path_within(root, args.links, "final links")

    fixture_rows = index_objects(fixture.get("rows"), "id", "fixture rows")
    evidence = index_objects(fixture.get("evidence"), "id", "fixture evidence")
    result_rows = index_objects(result.get("rows"), "id", "result rows")
    require(
        set(result_rows) == set(fixture_rows),
        "row results must cover every synthetic row exactly once",
    )

    contracts = {
        "RUNTIME-SUPPORT": ({"runtime-support-matrix"}, None, None),
        "BROWSER-SUPPORT": ({"browser-support-matrix"}, None, None),
        "SUPPORT-OWNERSHIP": (
            {"named-package-owner", "named-backup", "package-scope"},
            "owner evidence required",
            {"operation": "request-record", "record_type": "package-support-accountability"},
        ),
        "DISPOSAL-BEHAVIOR": (
            {"post-disposal-listener-observation"}, "not tested",
            {"operation": "run-probe", "probe": "post-disposal-listener-check"},
        ),
    }
    require(set(fixture_rows) == set(contracts), "row fixture questions are unsupported")
    record_facts = {}
    record_context = {}
    for evidence_id, record in evidence.items():
        facts = set()
        contexts = set()
        if record.get("published") is True:
            if record.get("runtime_versions"):
                facts.add("runtime-support-matrix")
            if record.get("browsers"):
                facts.add("browser-support-matrix")
        if "package_assignments" in record:
            contexts.add("SUPPORT-OWNERSHIP")
            for assignment in record["package_assignments"]:
                if assignment.get("package_id") != fixture["package_id"]:
                    continue
                if assignment.get("owner"):
                    facts.add("named-package-owner")
                if assignment.get("backup"):
                    facts.add("named-backup")
                if assignment.get("scope"):
                    facts.add("package-scope")
        if record.get("operation") == "post-disposal-listener-check":
            contexts.add("DISPOSAL-BEHAVIOR")
            if any(
                observation.get("phase") == "post-disposal"
                and isinstance(observation.get("listener_count"), int)
                and observation["listener_count"] >= 0
                for observation in record.get("observations", [])
            ):
                facts.add("post-disposal-listener-observation")
        record_facts[evidence_id] = facts
        record_context[evidence_id] = contexts

    for row_id, (required_facts, missing_status, next_action) in contracts.items():
        satisfying = sorted(
            evidence_id
            for evidence_id, facts in record_facts.items()
            if required_facts.issubset(facts)
        )
        if satisfying:
            expected_status = "verified"
            expected_evidence = satisfying
            expected_missing = []
            expected_next_action = None
        else:
            contextual = sorted(
                evidence_id
                for evidence_id in evidence
                if row_id in record_context[evidence_id]
                or required_facts.intersection(record_facts[evidence_id])
            )
            contextual_facts = {
                fact
                for evidence_id in contextual
                for fact in record_facts[evidence_id]
            }
            expected_missing = sorted(required_facts - contextual_facts)
            expected_evidence = contextual
            require(missing_status is not None, f"{row_id} fixture lacks its published matrix")
            expected_status = missing_status
            expected_next_action = next_action

        decision = result_rows[row_id]
        require(
            decision.get("status") == expected_status,
            f"{row_id} status must be '{expected_status}'",
        )
        require(
            sorted(decision.get("evidence_ids") or []) == expected_evidence,
            f"{row_id} evidence mapping does not match relevant facts",
        )
        require(
            sorted(decision.get("missing_facts") or []) == expected_missing,
            f"{row_id} missing facts are incorrect",
        )
        require(
            decision.get("next_action") == expected_next_action,
            f"{row_id} next action is incorrect",
        )

    required_roles = set(fixture.get("required_link_roles") or [])
    artifacts = index_objects(links.get("artifacts"), "role", "final artifacts")
    require(set(artifacts) == required_roles, "final links do not cover the required artifact roles")
    resolved = {}
    for role, artifact in artifacts.items():
        path = artifact.get("path")
        require(isinstance(path, str) and path, f"{role} needs a relative path")
        resolved[role] = resolve_link(root, path, role)
    require(
        resolved.get("row-decisions") == result_path,
        "row-decisions final link does not identify the verified decision artifact",
    )
    require(
        resolved.get("summary") == summary_path,
        "summary final link does not identify the requested summary artifact",
    )

    print("VALID row evidence mapping and nested paths")


def inventory_candidates(args):
    root = Path(args.root).resolve()
    package_path = Path(args.package_manifest).resolve()
    package = json.loads(package_path.read_text(encoding="utf-8"))
    package_id = package["package"]["package_id"]
    version = package["package"]["version"]
    acquisition = package["acquisition"]
    digest = package["package"]["nupkg_sha256"]["value"]
    base = f"packages/{package_id}/{version}"
    if acquisition == "release-candidate":
        base += f"/candidates/{digest}"
    components = []
    for value in args.component:
        component_id, manifest_path = value.split("=", 1)
        component_path = Path(manifest_path).resolve()
        manifest = json.loads(component_path.read_text(encoding="utf-8"))
        item = manifest["components"][0]
        assert item["id"] == component_id
        components.append(
            {
                "component_id": component_id,
                "input_manifest_path": component_path.relative_to(root).as_posix(),
                "revision_root": f"{base}/components/{component_id}/revisions",
                "render_modes": item["render_modes"],
                "exclusions": [],
            }
        )
    write_json(
        args.output,
        {
            "schema_version": 1,
            "packages": [
                {
                    "input_manifest_path": package_path.relative_to(root).as_posix(),
                    "revision_root": f"{base}/revisions",
                    "components": components,
                    "exclusions": [],
                }
            ],
        },
    )


GUIDANCE_PATH = "out/decision-guidance.md"
GUIDANCE_REPORT = "out/revisions/0001/package.report.md"
GUIDANCE_ROWS = {"PI-07", "CI-07", "CI-08"}


def guidance_check(condition, message):
    if not condition:
        raise ValueError(message)


def guidance_snapshot(path, case):
    # Both snapshots were produced by the validator, not hand-written validation receipts.
    # WorkflowTests revalidates the decoded five-file revisions on every supported OS.
    with zipfile.ZipFile(io.BytesIO(base64.b64decode(Path(path).read_text()))) as archive:
        files = {}
        for entry in archive.infolist():
            parts = entry.filename.split("/")
            guidance_check(
                len(parts) >= 2 and parts[0] in {"established", "insufficient"}
                and all(part not in {"", ".", ".."} and ":" not in part and "\\" not in part for part in parts)
                and not entry.is_dir() and entry.file_size < 1_000_000
                and (entry.external_attr >> 16) & 0o170000 != 0o120000,
                "invalid synthetic guidance snapshot entry",
            )
            if parts[0] == case:
                name = "/".join(parts[1:])
                guidance_check(name not in files, "duplicate synthetic guidance snapshot entry")
                files[name] = archive.read(entry)
        guidance_check(len(files) == 7, "expected two retained inputs and a five-file revision")
        return files


def prepare_guidance(args):
    for name, data in guidance_snapshot(args.snapshot, args.case).items():
        target = Path(args.root) / name
        guidance_check(not target.exists(), f"refusing to overwrite retained fixture {name}")
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)


def check_guidance_artifacts(root, snapshot, case, mode, output):
    root = Path(root)
    files = guidance_snapshot(snapshot, case)
    for name, data in files.items():
        path = root / name
        guidance_check(not path.is_symlink() and path.is_file() and path.read_bytes() == data,
                       f"retained bytes changed or missing: {name}")
    expected = set(files) | ({GUIDANCE_PATH} if mode == "recommendations" else set())
    expected_directories = {str(parent).replace("\\", "/") for name in expected
                            for parent in Path(name).parents if str(parent) != "."}
    actual_files, actual_directories = set(), set()
    for folder in ("retained", "out"):
        for path in [root / folder, *(root / folder).rglob("*")]:
            guidance_check(not path.is_symlink(), "unexpected symlink in retained output")
            relative = path.relative_to(root).as_posix()
            (actual_directories if path.is_dir() else actual_files).add(relative)
    guidance_check(actual_files == expected and actual_directories == expected_directories,
                   "unexpected revision, probe, output artifact or directory")
    companions = {path.relative_to(root).as_posix() for path in root.rglob("decision-guidance.md")}
    guidance_check(companions == ({GUIDANCE_PATH} if mode == "recommendations" else set()),
                   "factual-only request or unexpected guidance location")
    guidance_check(GUIDANCE_REPORT in output.replace("\\", "/"), "handoff omits existing factual report")
    if mode == "factual":
        return
    guidance_check(GUIDANCE_PATH in output.replace("\\", "/"), "handoff omits guidance link")
    text = (root / GUIDANCE_PATH).read_text(encoding="utf-8")
    manifest = files["out/revisions/0001/package.validation.json"]
    guidance_check(
        re.search(r"(?m)^Source validation manifest SHA-256: " + sha256_bytes(manifest) + r"\s*$", text),
        "guidance source validation-manifest digest differs",
    )
    guidance_check(set(re.findall(r"\b[A-Z][A-Z0-9]*-\d{2}\b", text)) == GUIDANCE_ROWS,
                   "guidance must cover only requested unresolved rows")
    guidance_check(("gap" if case == "established" else "not tested") in text,
                   "guidance loses retained finding status")
    for pattern in (
        r"(?i)(current finding|retained finding|current result)",
        r"(?i)(supported next step|next step|missing evidence)",
        r"(?i)(evidence.*reassess|reassess.*evidence)",
        r"(?i)(implementation references|verified references|sources)",
        r"(?i)(owner decision|owner.*limit)",
        r"(?i)(illustrative|not.*mandatory)",
        r"(?i)(not.*endorse|no.*endorse)",
        r"(?i)(not.*guarantee|no.*guarantee)",
        r"(?i)decomposition",
        r"(?i)versioned extension",
    ):
        guidance_check(re.search(pattern, text), f"guidance omits finding context: {pattern}")
    guidance_check("https://github.com/microsoft/sbom-tool" in text
                   and ("https://docs.github.com/" in text or "https://learn.microsoft.com/" in text),
                   "guidance omits verified SBOM and release references")
    if case == "insufficient":
        guidance_check(not re.search(
            r"(?im)^```[^\n]*\n\s*(?:sbom-tool|dotnet\s+nuget\s+(?:sign|push)|gh\s+attestation)\b", text),
            "missing evidence does not establish a tool/workflow prescription")


def guidance_write_path(value, workdir, mode):
    guidance_check(mode == "recommendations", "factual-only request must not write guidance")
    guidance_check(isinstance(value, str) and value.strip(), "write path unavailable")
    base = posixpath.normpath(workdir.replace("\\", "/"))
    value = value.replace("\\", "/")
    absolute = posixpath.normpath(value if value.startswith("/") or re.match(r"^[A-Za-z]:/", value)
                                 else posixpath.join(base, value))
    target = posixpath.join(base, GUIDANCE_PATH)
    if re.match(r"^[A-Za-z]:/", base):
        absolute, target = absolute.lower(), target.lower()
    guidance_check(absolute == target, f"write outside guidance target: {value}")


def guidance_matchers(eval_path):
    match = re.search(r"(?s)config: &guidance_local_calls\s*(\{.*?\n        \})", Path(eval_path).read_text())
    guidance_check(match is not None, "native guidance tool-call config unavailable")
    return json.loads(match[1])["disallowed"]


def check_guidance_trajectory(trajectory, mode, matchers):
    events = trajectory.get("events")
    guidance_check(isinstance(events, list) and events, "trajectory missing; no-rerun claim is unproven")
    guidance_check(all(isinstance(event, dict) and isinstance(event.get("data", {}), dict) for event in events),
                   "malformed trajectory event")
    calls = [event.get("data", {}) for event in events if event.get("type") == "tool_call"]
    results = [event.get("data", {}) for event in events if event.get("type") == "tool_result"]
    ids = [call.get("toolCallId") for call in calls]
    result_ids = [result.get("toolCallId") for result in results]
    guidance_check(calls and all(isinstance(value, str) and value for value in ids)
                   and all(isinstance(value, str) and value for value in result_ids)
                   and len(set(ids)) == len(ids)
                   and sorted(ids) == sorted(result_ids),
                   "incomplete tool-call/result trajectory; no-rerun claim is unproven")
    guidance_check(trajectory.get("metrics", {}).get("toolCallCount") == len(calls),
                   "tool-call count disagrees with recorded trajectory")
    guidance_check(not any(event.get("data", {}).get("simulated") for event in events),
                   "simulated calls do not prove actual behavior")
    guidance_check(not any(event.get("type") == "system" and event.get("data", {}).get("eventType")
                           in {"truncation", "snapshot_rewind"} for event in events),
                   "truncated/rewound trajectory requires complete raw-event review")
    workdir = trajectory.get("workDir")
    guidance_check(isinstance(workdir, str) and workdir, "trajectory work directory unavailable")

    def inspect(name, arguments):
        guidance_check(isinstance(name, str), "tool name unavailable")
        lower = name.lower()
        # Reuse the eval's native name/args matchers, including after masking literal example data.
        normalized = dict(arguments) if isinstance(arguments, dict) else {}
        for key in ("command", "cmd"):
            if isinstance(normalized.get(key), str):
                command = normalized[key]
                command = re.sub(r"(?ms)@'\r?\n.*?\r?\n'@", "'literal guidance text'", command)
                command = re.sub(r"(?ms)<<'([A-Za-z_][A-Za-z_0-9]*)'[^\n]*\n.*?\n\1(?:\n|$)", "", command)
                normalized[key] = command
        for matcher in matchers:
            if "write_only" in matcher:
                continue  # Native path extraction owns shell/editor write-target shapes.
            guidance_check(not (re.search(matcher["name"], name) and all(
                isinstance(normalized.get(key), str) and re.search(pattern, normalized[key])
                for key, pattern in matcher.get("args", {}).items())),
                f"disallowed assessment/probe/network call: {name}")
        if lower.endswith("parallel") and isinstance(arguments, dict):
            nested = arguments.get("tool_uses", [])
            guidance_check(isinstance(nested, list) and nested, "parallel tool evidence unavailable")
            for item in nested:
                inspect(item.get("recipient_name"), item.get("parameters"))
        elif re.search(r"(?:apply_patch)$", lower):
            guidance_check(isinstance(arguments, (str, dict)), "patch arguments unavailable")
            patch = arguments if isinstance(arguments, str) else arguments.get("patch", arguments.get("input", ""))
            guidance_check(isinstance(patch, str), "patch text unavailable")
            paths = re.findall(r"(?m)^\*\*\* (Add|Update|Delete|Move to) File: (.+)$", patch)
            guidance_check(paths and "*** Move to:" not in patch, "unproven patch targets")
            for operation, path in paths:
                guidance_check(operation in {"Add", "Update"}, "guidance deletion is not permitted")
                guidance_write_path(path.strip(), workdir, mode)
        elif re.search(r"(?:^|[._:-])(?:edit|create|write|multiedit)(?:_file)?$", lower):
            guidance_check(isinstance(arguments, dict), "write arguments unavailable")
            path = next((arguments[key] for key in ("path", "file_path", "filePath", "filename")
                         if key in arguments), None)
            guidance_write_path(path, workdir, mode)
    for call in calls:
        inspect(call.get("toolName"), call.get("arguments"))


def grade_guidance(args):
    try:
        payload = read_json_object(os.environ["EVALUATE_GRADER_INPUT"])
        trajectory = payload["trajectory"]
        check_guidance_trajectory(trajectory, args.mode, guidance_matchers(args.eval))
        check_guidance_artifacts(os.environ["EVALUATE_WORKSPACE"], args.snapshot, args.case, args.mode,
                                 trajectory.get("output", ""))
    except (KeyError, OSError, ValueError, zipfile.BadZipFile) as error:
        invalid(f"guidance contract: {error}")
    # Program-grader exit-code mode: success must not print non-JSON stdout.


def guidance_selftests(args):
    root = Path(args.scratch).resolve()
    guidance_check(not root.exists(), "guidance self-tests require fresh isolated scratch")
    root.mkdir(parents=True)
    count = 0
    matchers = guidance_matchers(args.eval)

    def trace(value, mode="recommendations"):
        check_guidance_trajectory(value, mode, matchers)

    def check(operation, expected_error=None):
        nonlocal count
        try:
            operation()
        except ValueError as error:
            guidance_check(expected_error is not None and expected_error in str(error),
                           f"unexpected guidance control failure: {error}")
        else:
            guidance_check(expected_error is None, f"negative guidance control accepted: {expected_error}")
        count += 1

    def trajectory(calls, output=GUIDANCE_REPORT + "\n" + GUIDANCE_PATH):
        events = []
        for index, (name, arguments) in enumerate(calls):
            identity = f"call-{index}"
            events.extend([
                {"type": "tool_call", "data": {"toolCallId": identity, "toolName": name, "arguments": arguments}},
                {"type": "tool_result", "data": {"toolCallId": identity, "toolName": name, "result": "completed"}},
            ])
        return {"events": events, "workDir": str(root), "output": output, "metrics": {"toolCallCount": len(calls)}}

    reads = [("read_file", {"path": GUIDANCE_REPORT}),
             ("powershell", {"command": "Get-Content out\\revisions\\0001\\package.assessment.json"}),
             ("bash", {"command": "sha256sum out/revisions/0001/package.validation.json"})]
    guide = """# Guidance
Source validation manifest SHA-256: DIGEST

## Current findings: PI-07, CI-07, CI-08
The retained status is STATUS. PI-07 is a decomposition evidence check.
CI-07 and CI-08 are versioned extensions, not baseline obligations.

### Supported next step
NEXT

### Evidence to support reassessment
Retain the release-bound SBOM and dependency inventory, exact job graph/permissions,
unsigned build/transfer digest, signed final digest and published digest comparison.

### Verified implementation references
https://github.com/microsoft/sbom-tool
https://learn.microsoft.com/nuget/create-packages/sign-a-package
https://docs.github.com/actions/how-tos/secure-your-work/use-artifact-attestations/use-artifact-attestations

### Owner decisions and limitations
Owners select trust, certificate and policy decisions. No unobserved execution is claimed.

### Authority disclaimer
These are illustrative options, not mandatory designs or endorsements.
There is no certification or guarantee of acceptance.
"""
    try:
        for case in ("established", "insufficient"):
            case_root = root / case
            prepare_guidance(SimpleNamespace(root=case_root, snapshot=args.snapshot, case=case))
            check(lambda: check_guidance_artifacts(case_root, args.snapshot, case, "factual", GUIDANCE_REPORT))
            digest = sha256_bytes((case_root / "out/revisions/0001/package.validation.json").read_bytes())
            text = guide.replace("DIGEST", digest).replace("STATUS", "gap" if case == "established" else "not tested")
            text = text.replace("NEXT", (
                "Include the omitted transitive dependency in the SBOM; separate untrusted build and trusted release jobs. "
                "Check the unsigned transfer, then sign, verify and compare the signed final/published digest. "
                "Signing changes bytes; unsigned and signed digests need not match."
                if case == "established" else
                "Request SBOM bytes and the dependency lock, the actual job graph and per-job permissions, "
                "plus exact artifact transfer/signing/publication records. No implementation cause is established."
            ))
            path = case_root / GUIDANCE_PATH
            path.write_text(text, encoding="utf-8")
            output = f"[Report]({GUIDANCE_REPORT}) [Advice]({GUIDANCE_PATH})"
            check(lambda: check_guidance_artifacts(case_root, args.snapshot, case, "recommendations", output))
            check(lambda: check_guidance_artifacts(case_root, args.snapshot, case, "factual", output),
                  "unexpected revision")
            check(lambda: check_guidance_artifacts(case_root, args.snapshot, case, "recommendations", GUIDANCE_REPORT),
                  "handoff omits guidance")
            for old, new, error in (
                (digest, "0" * 64, "source validation-manifest"),
                ("PI-07", "PI-06", "only requested unresolved"),
                ("https://github.com/microsoft/sbom-tool", "https://example.test/sbom", "verified SBOM"),
                ("Owner decisions", "Scope", "finding context"),
            ):
                path.write_text(text.replace(old, new), encoding="utf-8")
                check(lambda: check_guidance_artifacts(case_root, args.snapshot, case, "recommendations", output), error)
            path.write_text(text, encoding="utf-8")
            if case == "insufficient":
                path.write_text(text + "\n```text\nsbom-tool generate -b guessed-drop\n```\n", encoding="utf-8")
                check(lambda: check_guidance_artifacts(case_root, args.snapshot, case, "recommendations", output),
                      "does not establish a tool/workflow prescription")
                path.write_text(text, encoding="utf-8")
            report_path = case_root / GUIDANCE_REPORT
            original = report_path.read_bytes()
            report_path.write_bytes(original + b"\n")
            check(lambda: check_guidance_artifacts(case_root, args.snapshot, case, "recommendations", output),
                  "retained bytes changed")
            report_path.write_bytes(original)
            extra = case_root / "out/revisions/0002"
            extra.mkdir()
            check(lambda: check_guidance_artifacts(case_root, args.snapshot, case, "recommendations", output),
                  "unexpected revision")
            extra.rmdir()
            extra = case_root / "out/probe.txt"
            extra.write_text("unexpected probe", encoding="utf-8")
            check(lambda: check_guidance_artifacts(case_root, args.snapshot, case, "recommendations", output),
                  "unexpected revision")
            extra.unlink()
        check(lambda: trace(trajectory(reads), "factual"))
        check(lambda: trace(trajectory([("host.read_document", {"path": GUIDANCE_REPORT})]), "factual"))
        writes = [
            ("create", {"path": GUIDANCE_PATH, "content": "Example: dotnet build; curl; not executed."}),
            ("apply_patch", {"patch": "*** Begin Patch\n*** Add File: out/decision-guidance.md\n+dotnet build\n*** End Patch\n"}),
            ("bash", {"command": "cat > out/decision-guidance.md <<'EOF'\n```text\nsbom-tool generate -b example\ndotnet nuget sign example.nupkg\n```\nEOF\n"}),
            ("powershell", {"command": "@'\n```text\nsbom-tool generate -b example\ndotnet nuget sign example.nupkg\n```\n'@ | Set-Content -LiteralPath out\\decision-guidance.md"}),
            ("bash", {"command": """python3 -c "from pathlib import Path; p = Path('out/decision-guidance.md'); p.write_text('Example: dotnet build')" """}),
        ]
        for write in writes:
            check(lambda: trace(trajectory(reads + [write])))
            if write[0] in {"create", "apply_patch"}:
                check(lambda: trace(trajectory(reads + [write]), "factual"), "factual-only request")
        for name, arguments, error in (
            ("web_fetch", {"url": "https://example.test"}, "disallowed"),
            ("browser_navigate", {"url": "http://localhost"}, "disallowed"),
            ("bash", {"command": "bash readiness.sh assessment revise"}, "disallowed"),
            ("powershell", {"command": "dotnet build assessed.csproj"}, "disallowed"),
            ("bash", {"command": "dotnet run --project assessed.csproj"}, "disallowed"),
            ("bash", {"command": "dotnet test assessed.csproj"}, "disallowed"),
            ("powershell", {"command": "dotnet publish assessed.csproj"}, "disallowed"),
            ("exec_command", {"cmd": "dotnet test assessed.csproj"}, "disallowed"),
            ("bash", {"command": "curl https://example.test"}, "disallowed"),
            ("powershell", {"command": "Invoke-WebRequest https://example.test"}, "disallowed"),
            ("bash", {"command": "npx playwright test"}, "disallowed"),
            ("bash", {"command": """python3 -c "import subprocess; subprocess.run(['dotnet', 'build'])" """}, "disallowed"),
            ("create", {"path": "out/other.md", "content": "other"}, "outside guidance"),
            ("apply_patch", {"patch": "*** Begin Patch\n*** Delete File: out/decision-guidance.md\n*** End Patch\n"}, "deletion"),
        ):
            check(lambda: trace(trajectory(reads + [(name, arguments)])), error)
        for edit, error in (
            (lambda value: value.update(events=[]), "trajectory missing"),
            (lambda value: value["events"].pop(), "incomplete tool-call/result"),
            (lambda value: value["metrics"].update(toolCallCount=99), "tool-call count"),
            (lambda value: value["events"][0]["data"].update(simulated=True), "simulated calls"),
            (lambda value: value["events"].append({"type": "system", "data": {"eventType": "truncation"}}), "truncated"),
        ):
            value = trajectory(reads)
            edit(value)
            check(lambda: trace(value), error)
        write_pattern = next(item["pattern"] for item in matchers if item.get("write_only"))
        for path, prohibited in ((GUIDANCE_PATH, False), ("/workspace/" + GUIDANCE_PATH, False),
                                 ("out/other.md", True), ("out/revisions/0001/package.assessment.json", True)):
            check(lambda: guidance_check(bool(re.search(write_pattern, path)) == prohibited,
                                         "native write-target pattern disagrees"))
        check(lambda: guidance_write_path(r"C:\workspace\out\decision-guidance.md", r"C:\workspace", "recommendations"))
        check(lambda: guidance_write_path(r"C:\other\out\decision-guidance.md", r"C:\workspace", "recommendations"),
              "outside guidance")
        grader_input = root / "grader-input.json"
        grader_input.write_text(json.dumps({"trajectory": trajectory(reads + [writes[0]])}), encoding="utf-8")
        env = dict(os.environ, EVALUATE_WORKSPACE=str(root / "insufficient"), EVALUATE_GRADER_INPUT=str(grader_input))
        command = [sys.executable, str(Path(__file__).resolve()), "grade-guidance", "--snapshot",
                   str(Path(args.snapshot).resolve()), "--case", "insufficient", "--mode", "recommendations",
                   "--eval", str(Path(args.eval).resolve())]
        result = subprocess.run(command, env=env, capture_output=True, text=True, check=False)
        check(lambda: guidance_check(result.returncode == 0 and result.stdout == "" and result.stderr == "",
                                     f"program-grader interface failed: {result.returncode} {result.stderr}"))
        grader_input.write_text(json.dumps({"trajectory": {"events": []}}), encoding="utf-8")
        result = subprocess.run(command, env=env, capture_output=True, text=True, check=False)
        check(lambda: guidance_check(result.returncode == 1 and result.stdout == "" and "trajectory missing" in result.stderr,
                                     f"incomplete program-grader input accepted: {result.returncode} {result.stderr}"))
        print(f"VALID guidance controls {count}")
    finally:
        shutil.rmtree(root)


def main():
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(required=True)

    command = sub.add_parser("decode")
    command.add_argument("input")
    command.add_argument("output")
    command.set_defaults(func=decode)

    command = sub.add_parser("evidence-inputs")
    command.add_argument("--assessment", required=True)
    command.add_argument("--identity", required=True)
    command.add_argument("--subject", required=True)
    command.add_argument("--draft", required=True)
    command.add_argument("--claim", required=True)
    command.add_argument("--provenance", default="vendor-public-documentation")
    command.add_argument("--locator", default="https://docs.example.test/synthetic")
    command.add_argument("--digest", default="b" * 64)
    command.add_argument(
        "--method",
        default="Captured the supplied synthetic fixture without external access.",
    )
    command.add_argument("--second-claim")
    command.add_argument("--second-provenance", default="owner-supplied-internal-evidence")
    command.add_argument("--second-locator", default="owner-audit.txt")
    command.add_argument("--second-digest", default="c" * 64)
    command.set_defaults(func=evidence_inputs)

    command = sub.add_parser("complete")
    command.add_argument("--assessment", required=True)
    command.add_argument("--ledger", required=True)
    command.add_argument("--output", required=True)
    command.add_argument("--profile", choices=["default", "mixed"], default="default")
    command.set_defaults(func=complete)

    command = sub.add_parser("verify-revision")
    command.add_argument("--revision", required=True)
    command.add_argument("--kind", choices=["unified", "package", "component"], required=True)
    command.add_argument("--rows", type=int, required=True)
    command.set_defaults(func=verify_revision)

    command = sub.add_parser("verify-library")
    command.add_argument("--root", required=True)
    command.add_argument("--packages", type=int, required=True)
    command.add_argument("--components", type=int, required=True)
    command.set_defaults(func=verify_library)

    command = sub.add_parser("assert-row-not-verified")
    command.add_argument("--assessment", required=True)
    command.add_argument("--id", required=True)
    command.set_defaults(func=assert_row_not_verified)

    command = sub.add_parser("verify-targeted-decisions")
    command.add_argument("--input", required=True)
    command.set_defaults(func=verify_targeted_decisions)

    command = sub.add_parser("verify-provenance-decisions")
    command.add_argument("--input", required=True)
    command.set_defaults(func=verify_provenance_decisions)

    command = sub.add_parser("verify-dynamic")
    command.add_argument("--root", required=True)
    command.set_defaults(func=verify_dynamic)

    command = sub.add_parser("verify-blind-gate")
    command.add_argument("--root", required=True)
    command.add_argument("--fixture", required=True)
    command.set_defaults(func=verify_blind_gate)

    command = sub.add_parser("verify-candidate-binding")
    command.add_argument("--fixture", required=True)
    command.add_argument("--package-root", required=True)
    command.add_argument("--input", required=True)
    command.set_defaults(func=verify_candidate_binding)

    command = sub.add_parser("verify-release-records")
    command.add_argument("--fixture", required=True)
    command.add_argument("--input", required=True)
    command.set_defaults(func=verify_release_records)

    command = sub.add_parser("verify-ai-applicability")
    command.add_argument("--fixture", required=True)
    command.add_argument("--input", required=True)
    command.set_defaults(func=verify_ai_applicability)

    command = sub.add_parser("verify-row-evidence-map")
    command.add_argument("--fixture", required=True)
    command.add_argument("--root", required=True)
    command.add_argument("--input", required=True)
    command.add_argument("--summary", required=True)
    command.add_argument("--links", required=True)
    command.set_defaults(func=verify_row_evidence_map)

    command = sub.add_parser("inventory-candidates")
    command.add_argument("--root", required=True)
    command.add_argument("--package-manifest", required=True)
    command.add_argument("--component", action="append", required=True)
    command.add_argument("--output", required=True)
    command.set_defaults(func=inventory_candidates)

    for operation, handler in (("prepare-guidance", prepare_guidance), ("grade-guidance", grade_guidance)):
        command = sub.add_parser(operation)
        command.add_argument("--snapshot", required=True)
        command.add_argument("--case", choices=["established", "insufficient"], required=True)
        if operation == "prepare-guidance":
            command.add_argument("--root", required=True)
        else:
            command.add_argument("--mode", choices=["factual", "recommendations"], required=True)
            command.add_argument("--eval", required=True)
        command.set_defaults(func=handler)

    command = sub.add_parser("guidance-selftests")
    command.add_argument("--snapshot", required=True)
    command.add_argument("--scratch", required=True)
    command.add_argument("--eval", required=True)
    command.set_defaults(func=guidance_selftests)

    args = parser.parse_args()
    args.func(args)


if __name__ == "__main__":
    main()
