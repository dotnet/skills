#!/usr/bin/env python3
import argparse
import base64
import hashlib
import json
from pathlib import Path


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


def resolve_below(root, value):
    path = Path(value)
    assert not path.is_absolute()
    assert path.parts
    assert path.parts[0] not in (".", "out")
    resolved = (root / path).resolve()
    assert resolved == root or root in resolved.parents
    return resolved


def verify_worker_handoff(args):
    root = Path(args.root).resolve()
    handoff_path = (root / args.handoff).resolve()
    assert handoff_path.parent == root
    handoff = json.loads(handoff_path.read_text(encoding="utf-8"))
    assert set(handoff) == {
        "unit_id",
        "revision_path",
        "report_path",
        "validation_manifest_path",
        "validation_manifest_sha256",
        "blockers",
    }
    assert handoff["unit_id"] == args.unit_id
    assert handoff["blockers"] == []

    revision = resolve_below(root, handoff["revision_path"])
    report = resolve_below(root, handoff["report_path"])
    validation = resolve_below(root, handoff["validation_manifest_path"])
    assert report.parent == revision
    assert validation.parent == revision
    assert report.is_file()
    assert validation.is_file()
    assert hashlib.sha256(validation.read_bytes()).hexdigest() == handoff["validation_manifest_sha256"]

    assessments = list(revision.glob("*.assessment.json"))
    evidence = list(revision.glob("*.evidence.json"))
    reports = list(revision.glob("*.report.md"))
    validations = list(revision.glob("*.validation.json"))
    assert len(assessments) == len(evidence) == len(reports) == len(validations) == 1
    assert reports[0] == report
    assert validations[0] == validation

    assessment = json.loads(assessments[0].read_text(encoding="utf-8"))
    manifest = json.loads(validation.read_text(encoding="utf-8"))
    input_manifest = revision / "input-manifest.json"
    assert input_manifest.is_file()
    input_sha = hashlib.sha256(input_manifest.read_bytes()).hexdigest()
    assessment_sha = hashlib.sha256(assessments[0].read_bytes()).hexdigest()
    evidence_sha = hashlib.sha256(evidence[0].read_bytes()).hexdigest()
    report_sha = hashlib.sha256(report.read_bytes()).hexdigest()
    assert input_sha == args.input_sha
    assert manifest["input_manifest_sha256"]["value"] == input_sha
    assert manifest["assessment_sha256"]["value"] == assessment_sha
    assert manifest["evidence_sha256"]["value"] == evidence_sha
    assert manifest["report_sha256"]["value"] == report_sha
    assert assessment["identity"]["input_manifest_sha256"]["value"] == input_sha
    assert assessment["assessment_kind"] == args.kind
    assert assessment["identity"]["component_id"] == args.component
    assert len(assessment["rows"]) == args.rows
    assert assessment["completion_state"] == "complete"
    assert manifest["assessment_kind"] == args.kind
    assert manifest["completion_state"] == "complete"
    assert (
        assessment["package_reference"]["validation_sha256"]["value"]
        == args.package_validation_sha
    )
    assert (
        manifest["package_reference"]["validation_sha256"]["value"]
        == args.package_validation_sha
    )

    if args.forbid_text:
        forbidden = args.forbid_text.casefold().encode("utf-8")
        for path in revision.iterdir():
            if path.is_file():
                assert forbidden not in path.name.casefold().encode("utf-8")
                assert forbidden not in path.read_bytes().lower()

    print(
        f"VALID worker handoff {args.component} {args.kind} {args.rows} "
        f"{handoff['validation_manifest_sha256']}"
    )


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
    verify = argparse.Namespace(revision=str(revision), kind="unified", rows=110)
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

    command = sub.add_parser("verify-worker-handoff")
    command.add_argument("--root", required=True)
    command.add_argument("--handoff", required=True)
    command.add_argument("--kind", choices=["component"], required=True)
    command.add_argument("--rows", type=int, required=True)
    command.add_argument("--unit-id", required=True)
    command.add_argument("--component", required=True)
    command.add_argument("--input-sha", required=True)
    command.add_argument("--package-validation-sha", required=True)
    command.add_argument("--forbid-text")
    command.set_defaults(func=verify_worker_handoff)

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

    command = sub.add_parser("inventory-candidates")
    command.add_argument("--root", required=True)
    command.add_argument("--package-manifest", required=True)
    command.add_argument("--component", action="append", required=True)
    command.add_argument("--output", required=True)
    command.set_defaults(func=inventory_candidates)

    args = parser.parse_args()
    args.func(args)


if __name__ == "__main__":
    main()
