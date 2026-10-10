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
        if row["id"] == "BEQ-05":
            row.update(
                status="not tested",
                assessment_follow_up="No static-SSR behavior observation is supplied by this structural fixture.",
                not_applicable_rationale=None,
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
    assert args.kind in {"package", "component"}
    assert assessment["schema_version"] == 2 and assessment["rubric_version"] == "2.1.0"
    assert assessment["assessment_kind"] == args.kind
    assert len(assessment["rows"]) == args.rows
    assert assessment["completion_state"] == "complete"
    assert manifest["assessment_kind"] == args.kind
    assert manifest["completion_state"] == "complete"
    expected_scope = "repository-wide" if args.kind == "package" else "component-specific"
    assert all(row["scope"] == expected_scope for row in assessment["rows"])
    assert len({row["id"] for row in assessment["rows"]}) == args.rows
    assert not {row["id"] for row in assessment["rows"]} & {
        "CI-02", "CI-03", "CI-04", "CI-09", "CI-10",
        "PERF-07", "PERF-08", "PERF-09", "PERF-10",
    }
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


def embedded_sbom_snapshot(path, case):
    with zipfile.ZipFile(io.BytesIO(base64.b64decode(Path(path).read_text()))) as archive:
        prefix = case + "/"
        files = {name[len(prefix):]: archive.read(name) for name in archive.namelist() if name.startswith(prefix)}
    guidance_check(set(files) == {"fixture/package.nupkg", "fixture/package.spdx.json",
                                 "fixture/package-entries.sha256", "fixture/release.sha256"},
                   "embedded fixture must contain only the four raw release files")
    return files


def prepare_embedded_sbom(args):
    for name, data in embedded_sbom_snapshot(args.snapshot, args.case).items():
        target = Path(args.root) / name
        guidance_check(not target.exists(), "refusing to overwrite embedded fixture inputs")
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)


def inspect_embedded_sbom(files):
    package = files["fixture/package.nupkg"]
    with zipfile.ZipFile(io.BytesIO(package)) as archive:
        entries = {name: archive.read(name) for name in archive.namelist()}
    for name, data in entries.items():
        guidance_check(f"{sha256_bytes(data)}  {name}\n" in files["fixture/package-entries.sha256"].decode(),
                       "package-entry digest differs")
    for name in ("package.nupkg", "package.spdx.json", "package-entries.sha256"):
        guidance_check(f"{sha256_bytes(files['fixture/' + name])}  {name}\n" in files["fixture/release.sha256"].decode(),
                       "release digest differs")
    source_map = json.loads(entries["staticwebassets/ui.js.map"])
    content = source_map["sourcesContent"]
    guidance_check(source_map["version"] == 3
                   and source_map["sources"] == ["node_modules/string-ribbon/index.js",
                                                "src/widgets/range.js", "src/widgets/present.js"]
                   and len(content) == 3 and all(isinstance(item, str) and item for item in content)
                   and source_map["mappings"] == "AACA;ACAA;ACAA",
                   "distributed map/content evidence differs")
    bundle = "".join(item.splitlines(keepends=True)[1] for item in content) + "//# sourceMappingURL=ui.js.map\n"
    bundle_corresponds = source_map["file"] == "ui.js" and entries["staticwebassets/ui.js"].decode() == bundle
    sbom = json.loads(files["fixture/package.spdx.json"])
    packages = {item["SPDXID"]: item for item in sbom["packages"]}
    package_record = packages["SPDXRef-Package"]
    guidance_check(package_record["name"] == "Sample.UiKit" and package_record["versionInfo"] == "1.4.0"
                   and package_record["checksums"] == [{"algorithm": "SHA256", "checksumValue": sha256_bytes(package)}]
                   and b"<id>Sample.UiKit</id><version>1.4.0</version>" in entries["Sample.UiKit.nuspec"],
                   "SBOM/package identity differs")
    sbom_files = {item["SPDXID"]: item for item in sbom["files"]}

    def represented(identity, source_index):
        targets = {item["relatedSpdxElement"] for item in sbom["relationships"]
                   if item["spdxElementId"] == identity and item["relationshipType"] == "CONTAINS"}
        return any(sbom_files[target]["fileName"] == source_map["sources"][source_index]
                   and sbom_files[target]["checksums"] == [
                       {"algorithm": "SHA256", "checksumValue": sha256_bytes(content[source_index].encode())}]
                   for target in targets)

    for identity, source_index in (("SPDXRef-String", 0), ("SPDXRef-Range", 1)):
        if represented(identity, source_index):
            component = packages[identity]
            purl = f"pkg:npm/{component['name']}@{component['versionInfo']}"
            guidance_check(purl in content[source_index]
                           and any(item["referenceType"] == "purl" and item["referenceLocator"] == purl
                                   for item in component["externalRefs"]),
                           "embedded component identity/version differs")
    guidance_check("pkg:npm/string-ribbon@2.3.0" in content[0]
                   and packages["SPDXRef-String"]["versionInfo"] == "2.3.0"
                   and represented("SPDXRef-String", 0), "ordinary npm correspondence differs")
    guidance_check("Independent Range Authors" in content[1]
                   and ("Original synthetic upstream:" in content[1] or "Third-party source" in content[1]),
                   "third-party distribution is not established")
    return content[1], packages, represented("SPDXRef-Range", 1), bundle_corresponds


def embedded_sbom_selftests(args):
    root = Path(args.scratch).resolve()
    guidance_check(not root.exists(), "embedded SBOM self-tests require fresh scratch")
    root.mkdir(parents=True)
    count = 0

    def check(condition, label):
        nonlocal count
        guidance_check(condition, label)
        count += 1

    try:
        match = re.search(r"(?m)^            - &embedded_sbom_grade \|\n((?:              .*\n)+)",
                          Path(args.eval).read_text())
        guidance_check(match is not None, "embedded outcome grader unavailable")
        code = "".join(line[14:] for line in match[1].splitlines(keepends=True))
        for case, expected in (("release-01", "verified"), ("release-02", "gap"), ("release-03", "not tested")):
            files = embedded_sbom_snapshot(args.snapshot, case)
            embedded, packages, represented, corresponds = inspect_embedded_sbom(files)
            check(corresponds, "the three raw cases retain their bundle/map correspondence")
            case_root = root / case
            prepare_embedded_sbom(SimpleNamespace(root=case_root, snapshot=args.snapshot, case=case))
            check(all((case_root / name).read_bytes() == data for name, data in files.items()),
                  "raw materialization differs")
            if case == "release-01":
                check("pkg:npm/range-lantern@0.9.2" in embedded and represented
                      and packages["SPDXRef-Range"]["versionInfo"] == "0.9.2", "represented component differs")
            elif case == "release-02":
                check("pkg:npm/range-lantern@0.9.2" in embedded and not represented
                      and "SPDXRef-Range" not in packages, "omission is not established")
            else:
                check("pkg:npm/" not in embedded and "module identifier and source revision were not retained" in embedded
                      and "SPDXRef-Range" in packages and not represented, "plausible coverage must remain inconclusive")
            changed = dict(files, **{"fixture/package.nupkg": files["fixture/package.nupkg"] + b"\0"})
            try:
                inspect_embedded_sbom(changed)
            except ValueError as error:
                check("release digest" in str(error), "mismatched distribution was not rejected")
            else:
                raise ValueError("mismatched package accepted")
            with zipfile.ZipFile(io.BytesIO(files["fixture/package.nupkg"])) as archive:
                entries = {name: archive.read(name) for name in archive.namelist()}

            def repack(changed_entries):
                stream = io.BytesIO()
                with zipfile.ZipFile(stream, "w") as archive:
                    for name, data in changed_entries.items():
                        archive.writestr(name, data)
                sbom = json.loads(files["fixture/package.spdx.json"])
                next(item for item in sbom["packages"] if item["SPDXID"] == "SPDXRef-Package")["checksums"] = [
                    {"algorithm": "SHA256", "checksumValue": sha256_bytes(stream.getvalue())}]
                for item in sbom["files"]:
                    if item["fileName"] in changed_entries:
                        item["checksums"] = [{"algorithm": "SHA256",
                                              "checksumValue": sha256_bytes(changed_entries[item["fileName"]])}]
                changed = dict(files, **{
                    "fixture/package.nupkg": stream.getvalue(),
                    "fixture/package.spdx.json": json.dumps(sbom).encode(),
                    "fixture/package-entries.sha256": "".join(
                        f"{sha256_bytes(data)}  {name}\n" for name, data in sorted(changed_entries.items())).encode(),
                })
                changed["fixture/release.sha256"] = "".join(
                    f"{sha256_bytes(changed['fixture/' + name])}  {name}\n"
                    for name in ("package.nupkg", "package.spdx.json", "package-entries.sha256")).encode()
                return changed

            wrong_file = dict(json.loads(entries["staticwebassets/ui.js.map"]), file="not-ui.js")
            changed = repack(dict(entries, **{"staticwebassets/ui.js.map": json.dumps(wrong_file).encode()}))
            mapped, _, covered, linked = inspect_embedded_sbom(changed)
            check(mapped == embedded and covered == represented and not linked,
                  "a mismatched bundle reference does not erase distributed map bytes")
            orphan = repack(dict(entries, **{"staticwebassets/ui.js": b"export const fixtureVersion = 1;\n"}))
            mapped, _, covered, linked = inspect_embedded_sbom(orphan)
            check(mapped == embedded and covered == represented and not linked,
                  "an orphaned distributed map retains its attribution and representation boundary")
            source_only = dict(json.loads(entries["staticwebassets/ui.js.map"]), sourcesContent=[None, None, None])
            changed = repack(dict(entries, **{"staticwebassets/ui.js.map": json.dumps(source_only).encode()}))
            try:
                inspect_embedded_sbom(changed)
            except ValueError as error:
                check("map/content evidence" in str(error), "source-only paths accepted as distributed source bytes")
            else:
                raise ValueError("source-only paths accepted as distributed source bytes")

            controls = case_root / "controls"
            shutil.copytree(Path(args.controls) / case / "generated", controls)
            revision = controls / "revisions/0001"
            result_path = revision / "package.assessment.json"
            original = result_path.read_bytes()
            assessment = json.loads(original)

            def grade():
                result = subprocess.run([sys.executable, "-I", "-c", code, str(result_path), expected],
                                        capture_output=True, text=True, check=False)
                return result.returncode == 0

            check(grade(), "untouched native-generated revision and reader must pass")
            for status in STATUSES:
                if status == expected:
                    continue
                assessment = json.loads(original)
                next(row for row in assessment["rows"] if row["id"] == "PI-08")["status"] = status
                result_path.write_text(json.dumps(assessment), encoding="utf-8")
                check(not grade(), "outcome grader accepts the wrong status")
            assessment = json.loads(original)
            next(row for row in assessment["rows"] if row["id"] == "PI-08")["evidence_ids"] = []
            result_path.write_text(json.dumps(assessment), encoding="utf-8")
            check(not grade(), "unsupported result accepted")
            assessment = json.loads(original)
            assessment["rows"][0]["id"] = "CONTROL-01"
            result_path.write_text(json.dumps(assessment), encoding="utf-8")
            check(not grade(), "arbitrary replacement for a canonical ID accepted")
            assessment = json.loads(original)
            assessment["rows"].reverse()
            result_path.write_text(json.dumps(assessment), encoding="utf-8")
            check(not grade(), "noncanonical row order accepted")
            result_path.write_bytes(original)
            for path in (revision / "package.validation.json", controls / "readable/reader.validation.json"):
                retained = path.read_bytes()
                path.write_text("{}", encoding="utf-8")
                check(not grade(), "empty validation sidecar accepted")
                path.write_bytes(retained)
            check(grade(), "restored native control must still pass")
        print(f"VALID embedded SBOM controls {count}")
    finally:
        shutil.rmtree(root)


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
    assessment = json.loads((revision / "component.assessment.json").read_text(encoding="utf-8"))
    evidence = json.loads((revision / "component.evidence.json").read_text(encoding="utf-8"))
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
    verify = argparse.Namespace(revision=str(revision), kind="component", rows=52)
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
GUIDANCE_GRADING_FILES = {".grading/guidance-tool.py", ".grading/retained-hashes.json"}
GUIDANCE_READ_TOOL = re.compile(
    r"(?i)(?:^|[._:-])(?:read|read_file|read_document|view|glob|rg|grep|"
    r"search_files|list_files|list_directory)$"
)
GUIDANCE_WRITE_TOOL = re.compile(
    r"(?i)(?:^|[._:-])(?:edit|create|write|multiedit|apply_patch|patch_text)(?:_file)?$"
)
GUIDANCE_PATCH_TOOL = re.compile(r"(?i)(?:^|[._:-])(?:apply_patch|patch_text)$")


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
    files = guidance_snapshot(args.snapshot, args.case)
    for name, data in files.items():
        target = Path(args.root) / name
        guidance_check(not target.exists(), f"refusing to overwrite retained fixture {name}")
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
    if getattr(args, "grading_files", False):
        grading = Path(args.root) / ".grading"
        guidance_check(not grading.exists(), "refusing to overwrite grader inputs")
        grading.mkdir()
        (grading / "guidance-tool.py").write_bytes(Path(__file__).read_text(encoding="utf-8").encode("utf-8"))
        (grading / "retained-hashes.json").write_bytes(guidance_hash_manifest(files))


def guidance_hash_manifest(files):
    return json.dumps({name: sha256_bytes(files[name]) for name in sorted(files)},
                      separators=(",", ":")).encode("utf-8")


def check_guidance_artifacts(root, snapshot, case, mode, output, snapshot_sha256=None):
    root = Path(root)
    if snapshot_sha256 is None:
        files = json.loads(guidance_hash_manifest(guidance_snapshot(snapshot, case)))
    else:
        data = Path(snapshot).read_bytes()
        guidance_check(sha256_bytes(data) == snapshot_sha256, "retained hash manifest differs from trusted grader config")
        files = json.loads(data)
    for name, digest in files.items():
        path = root / name
        guidance_check(not path.is_symlink() and path.is_file() and sha256_bytes(path.read_bytes()) == digest,
                       f"retained bytes changed or missing: {name}")
    expected = set(files) | GUIDANCE_GRADING_FILES | ({GUIDANCE_PATH} if mode == "recommendations" else set())
    expected_directories = {str(parent).replace("\\", "/") for name in expected
                            for parent in Path(name).parents if str(parent) != "."}
    actual_files, actual_directories = set(), set()
    # This proves final state inside EVALUATE_WORKSPACE, not transient or out-of-workspace effects.
    for path in root.rglob("*"):
        guidance_check(not path.is_symlink(), "unexpected symlink in evaluation workspace")
        relative = path.relative_to(root).as_posix()
        (actual_directories if path.is_dir() else actual_files).add(relative)
    unexpected_files = sorted(actual_files ^ expected)
    unexpected_directories = sorted(actual_directories ^ expected_directories)
    guidance_check(
        not unexpected_files and not unexpected_directories,
        f"unexpected evaluation workspace artifact or directory: "
        f"files={unexpected_files[:5]} directories={unexpected_directories[:5]}",
    )
    companions = {path.relative_to(root).as_posix() for path in root.rglob("decision-guidance.md")}
    guidance_check(companions == ({GUIDANCE_PATH} if mode == "recommendations" else set()),
                   "factual-only request or unexpected guidance location")
    guidance_check(GUIDANCE_REPORT in output.replace("\\", "/"), "handoff omits existing factual report")
    if mode == "factual":
        return
    guidance_check(GUIDANCE_PATH in output.replace("\\", "/"), "handoff omits guidance link")
    text = (root / GUIDANCE_PATH).read_text(encoding="utf-8")
    manifest_digest = files["out/revisions/0001/package.validation.json"]
    guidance_check(
        re.search(r"(?m)^Source validation manifest SHA-256: " + manifest_digest + r"\s*$", text),
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


def guidance_matchers(eval_path):
    text = Path(eval_path).read_text()
    match = re.search(r"(?s)config: &guidance_local_calls\s*(\{.*?\n        \})", text)
    guidance_check(match is not None, "native guidance tool-call config unavailable")
    local_matchers = json.loads(match[1])["disallowed"]
    guidance_check(len(local_matchers) == 3 and all(set(item) == {"name"} for item in local_matchers),
                   "native guidance name matcher set changed")
    write_match = re.search(
        r"(?m)^\s+- name: '(\(\?:\^\|\[._:-\]\)\(\?:\[Ee\]dit.*apply_patch.*)'$",
        text,
    )
    guidance_check(write_match is not None, "native factual write matcher unavailable")
    return local_matchers, [{"name": write_match[1]}]


def guidance_native_disallowed(name, arguments, matchers):
    return any(
        re.search(matcher["name"], name)
        and all(isinstance(arguments.get(key), str) and re.search(pattern, arguments[key])
                for key, pattern in matcher.get("args", {}).items())
        for matcher in matchers
    )


def guidance_write_path(value, workdir):
    guidance_check(isinstance(value, str) and value.strip(), "write path unavailable")
    base = posixpath.normpath(workdir.replace("\\", "/"))
    value = value.replace("\\", "/")
    absolute = posixpath.normpath(value if value.startswith("/") or re.match(r"^[A-Za-z]:/", value)
                                 else posixpath.join(base, value))
    target = posixpath.join(base, GUIDANCE_PATH)
    if re.match(r"^[A-Za-z]:/", base):
        absolute, target = absolute.lower(), target.lower()
    guidance_check(absolute == target, f"write outside guidance target: {value}")


def check_guidance_trajectory(trajectory, mode):
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
    write_count = 0
    for call in calls:
        name = call.get("toolName")
        arguments = call.get("arguments")
        guidance_check(isinstance(name, str), "tool-call evidence unavailable")
        if GUIDANCE_READ_TOOL.search(name):
            continue
        guidance_check(GUIDANCE_WRITE_TOOL.search(name), f"unproven tool effect: {name}")
        guidance_check(mode == "recommendations", "factual-only request used a write tool")
        guidance_check(isinstance(arguments, dict), "write tool arguments unavailable")
        if GUIDANCE_PATCH_TOOL.search(name):
            patch = next((arguments[key] for key in ("patch", "input") if key in arguments), None)
            guidance_check(isinstance(patch, str), "patch target unavailable")
            headers = re.findall(
                r"(?m)^\*\*\* (?:(Add|Update|Delete) File: |(Move to): )(.+)$",
                patch,
            )
            guidance_check(len(headers) == 1, "patch must expose exactly one target")
            operation, move, path = headers[0]
            guidance_check(not move and operation in {"Add", "Update"}, "patch delete or move is not permitted")
            guidance_write_path(path.strip(), workdir)
        else:
            paths = [arguments[key] for key in ("path", "file_path", "filePath", "filename")
                     if key in arguments]
            guidance_check(len(paths) == 1, "write tool must expose exactly one target")
            guidance_write_path(paths[0], workdir)
        write_count += 1
    guidance_check(mode == "factual" or write_count > 0, "recommendations guidance write tool missing")


def grade_guidance(args):
    try:
        payload = read_json_object(os.environ["EVALUATE_GRADER_INPUT"])
        trajectory = payload["trajectory"]
        check_guidance_trajectory(trajectory, args.mode)
        check_guidance_artifacts(os.environ["EVALUATE_WORKSPACE"], args.snapshot, args.case, args.mode,
                                 trajectory.get("output", ""), args.snapshot_sha256)
    except (KeyError, StopIteration, OSError, ValueError, zipfile.BadZipFile) as error:
        invalid(f"guidance contract: {error}")
    # Program-grader exit-code mode: success must not print non-JSON stdout.


def guidance_selftests(args):
    root = Path(args.scratch).resolve()
    guidance_check(not root.exists(), "guidance self-tests require fresh isolated scratch")
    root.mkdir(parents=True)
    count = 0
    local_matchers, factual_write_matchers = guidance_matchers(args.eval)

    def trace(value, mode="recommendations"):
        check_guidance_trajectory(value, mode)

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
             ("functions.view", {"path": "out/revisions/0001/package.assessment.json"}),
             ("rg", {"pattern": "PI-07", "path": "out/revisions/0001/package.validation.json"})]
    guide = """# Guidance
Source validation manifest SHA-256: DIGEST

## Current findings: PI-07, CI-07, CI-08
The retained status is STATUS. PI-07 is a decomposition evidence check.
CI-07 and CI-08 are versioned extensions, not baseline obligations.

### Supported next step
NEXT

### Evidence to support reassessment
Synthetic reassessment-evidence placeholder, not a remediation answer.

### Verified implementation references
https://github.com/microsoft/sbom-tool
https://learn.microsoft.com/nuget/create-packages/sign-a-package
https://docs.github.com/actions/how-tos/secure-your-work/use-artifact-attestations/use-artifact-attestations

### Owner decisions and limitations
Synthetic owner-decision placeholder. No unobserved execution is claimed.

### Authority disclaimer
These are illustrative options, not mandatory designs or endorsements.
There is no certification or guarantee of acceptance.
"""
    try:
        for case in ("established", "insufficient"):
            case_root = root / case
            prepare_guidance(SimpleNamespace(root=case_root, snapshot=args.snapshot, case=case, grading_files=True))
            check(lambda: check_guidance_artifacts(case_root, args.snapshot, case, "factual", GUIDANCE_REPORT))
            digest = sha256_bytes((case_root / "out/revisions/0001/package.validation.json").read_bytes())
            text = guide.replace("DIGEST", digest).replace("STATUS", "gap" if case == "established" else "not tested")
            text = text.replace("NEXT", "Synthetic next-step placeholder; this control tests artifact boundaries, not advice quality.")
            path = case_root / GUIDANCE_PATH
            path.write_text(text, encoding="utf-8")
            output = f"[Report]({GUIDANCE_REPORT}) [Advice]({GUIDANCE_PATH})"
            check(lambda: check_guidance_artifacts(case_root, args.snapshot, case, "recommendations", output))
            check(lambda: check_guidance_artifacts(case_root, args.snapshot, case, "factual", output),
                  "unexpected evaluation workspace")
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
                  "unexpected evaluation workspace")
            extra.rmdir()
            extra = case_root / "out/probe.txt"
            extra.write_text("unexpected probe", encoding="utf-8")
            check(lambda: check_guidance_artifacts(case_root, args.snapshot, case, "recommendations", output),
                  "unexpected evaluation workspace")
            extra.unlink()
            outside = case_root / "outside.md"
            outside.write_text("unexpected persistent artifact", encoding="utf-8")
            check(lambda: check_guidance_artifacts(case_root, args.snapshot, case, "recommendations", output),
                  "outside.md")
            outside.unlink()
        check(lambda: trace(trajectory(reads), "factual"))
        check(lambda: trace(trajectory([("host.read_document", {"path": GUIDANCE_REPORT})]), "factual"))
        for name, arguments in (
            ("bash", {"command": "Get-Content report"}),
            ("functions.powershell", {"command": "Get-Content report"}),
            ("terminal", {}),
            ("namespace.exec_command", {"cmd": "cat report"}),
            ("tool.run_command", {"command": "cat report"}),
            ("web_fetch", {"url": "https://example.test"}),
            ("mcp.web_search", {"query": "example"}),
            ("fetch", {"url": "https://example.test"}),
            ("browser.navigate", {"url": "https://example.test"}),
            ("computer-use.click", {"x": 1, "y": 1}),
            ("task", {"prompt": "inspect"}),
            ("functions.subagent", {"prompt": "inspect"}),
            ("create_session", {"prompt": "inspect"}),
            ("run_factory", {"name": "inspect"}),
            ("run_workflow", {"workflow_id": "inspect"}),
        ):
            check(lambda: guidance_check(guidance_native_disallowed(name, arguments, local_matchers),
                                         f"native matcher missed prohibited tool: {name}"))
        for name, arguments in (
            ("read_file", {"path": GUIDANCE_REPORT}),
            ("functions.view", {"path": GUIDANCE_REPORT}),
            ("rg", {"pattern": "PI-07", "path": GUIDANCE_REPORT}),
            ("create", {"path": GUIDANCE_PATH}),
        ):
            check(lambda: guidance_check(not guidance_native_disallowed(name, arguments, local_matchers),
                                         f"native matcher rejected allowed tool: {name}"))
        for name in ("create", "functions.edit_file", "apply_patch", "patch_text"):
            check(lambda: guidance_check(
                guidance_native_disallowed(name, {"path": GUIDANCE_PATH}, factual_write_matchers),
                f"native factual write matcher missed {name}"))
        writes = [
            ("create", {"path": GUIDANCE_PATH, "content": "guidance"}),
            ("functions.edit_file", {"file_path": GUIDANCE_PATH, "content": "guidance"}),
            ("apply_patch", {"patch": "*** Begin Patch\n*** Add File: out/decision-guidance.md\n+guidance\n*** End Patch\n"}),
            ("patch_text", {"patch": "*** Begin Patch\n*** Update File: out/decision-guidance.md\n@@\n-old\n+new\n*** End Patch\n"}),
        ]
        for write in writes:
            check(lambda: trace(trajectory(reads + [write])))
            check(lambda: trace(trajectory(reads + [write]), "factual"), "factual-only request")
        check(lambda: trace(trajectory(reads + [("create", {"path": "outside.md"})])),
              "outside guidance")
        check(lambda: trace(trajectory(reads + [("create", {})])),
              "exactly one target")
        check(lambda: trace(trajectory(reads + [("create", {
            "path": GUIDANCE_PATH,
            "file_path": GUIDANCE_PATH,
        })])), "exactly one target")
        check(lambda: trace(trajectory(reads + [("apply_patch", {
            "patch": "*** Begin Patch\n*** Add File: outside.md\n+x\n*** End Patch\n",
        })])), "outside guidance")
        check(lambda: trace(trajectory(reads + [("apply_patch", {
            "patch": "*** Begin Patch\n*** Delete File: out/decision-guidance.md\n*** End Patch\n",
        })])), "delete or move")
        check(lambda: trace(trajectory(reads + [("apply_patch", {
            "patch": "*** Begin Patch\n*** Add File: out/decision-guidance.md\n+x\n"
                     "*** Update File: out/decision-guidance.md\n@@\n-x\n+y\n*** End Patch\n",
        })])), "exactly one target")
        check(lambda: trace(trajectory(reads + [("apply_patch", {"patch": "*** Begin Patch\n*** End Patch\n"})])),
              "exactly one target")
        check(lambda: trace(trajectory(reads + [("unknown_local_tool", {})])),
              "unproven tool effect")
        check(lambda: trace(trajectory(reads + [("bash", {"command": "dotnet build"})])),
              "unproven tool effect")
        check(lambda: trace(trajectory(reads + [("create", None)])),
              "write tool arguments unavailable")
        check(lambda: trace(trajectory(reads)), "guidance write tool missing")
        for edit, error in (
            (lambda value: value.update(events=[]), "trajectory missing"),
            (lambda value: value["events"].pop(), "incomplete tool-call/result"),
            (lambda value: value["metrics"].update(toolCallCount=99), "tool-call count"),
            (lambda value: value["events"][0]["data"].update(simulated=True), "simulated calls"),
            (lambda value: value["events"].append({"type": "system", "data": {"eventType": "truncation"}}), "truncated"),
        ):
            value = trajectory(reads)
            edit(value)
            check(lambda: trace(value, "factual"), error)
        grader_input = root / "grader-input.json"
        grader_input.write_text(json.dumps({"trajectory": trajectory(reads + [writes[0]])}), encoding="utf-8")
        env = dict(os.environ, EVALUATE_WORKSPACE=str(root / "insufficient"), EVALUATE_GRADER_INPUT=str(grader_input))
        hashes = guidance_hash_manifest(guidance_snapshot(args.snapshot, "insufficient"))
        hash_file = root / "retained-hashes.json"
        hash_file.write_bytes(hashes)
        command = [sys.executable, str(Path(__file__).resolve()), "grade-guidance", "--snapshot",
                   str(hash_file), "--snapshot-sha256", sha256_bytes(hashes),
                   "--case", "insufficient", "--mode", "recommendations"]
        result = subprocess.run(command, env=env, capture_output=True, text=True, check=False)
        check(lambda: guidance_check(result.returncode == 0 and result.stdout == "" and result.stderr == "",
                                     f"program-grader interface failed: {result.returncode} {result.stderr}"))
        hash_file.write_bytes(hashes + b"\n")
        result = subprocess.run(command, env=env, capture_output=True, text=True, check=False)
        check(lambda: guidance_check(result.returncode == 1 and "trusted grader config" in result.stderr,
                                     "tampered retained hash manifest accepted"))
        hash_file.write_bytes(hashes)
        grader_input.write_text(json.dumps({"trajectory": {"events": []}}), encoding="utf-8")
        result = subprocess.run(command, env=env, capture_output=True, text=True, check=False)
        check(lambda: guidance_check(result.returncode == 1 and result.stdout == "" and "trajectory missing" in result.stderr,
                                     f"incomplete program-grader input accepted: {result.returncode} {result.stderr}"))
        print(f"VALID guidance controls {count}")
    finally:
        shutil.rmtree(root)


def requested_help_selftests(args):
    root = Path(args.scratch)
    guidance_check(not root.exists(), "requested-help scratch must be new")
    root.mkdir(parents=True)
    count = 0

    def check(action, reject=False):
        nonlocal count
        try:
            action()
        except ValueError:
            guidance_check(reject, "valid requested-help control was rejected")
        else:
            guidance_check(not reject, "requested-help side effect was accepted")
        count += 1

    def unchanged(directory, expected):
        paths = list(directory.rglob("*"))
        guidance_check(all(path.is_file() and not path.is_symlink() for path in paths),
                       "advice created a directory or linked artifact")
        actual = {path.relative_to(directory).as_posix(): sha256_bytes(path.read_bytes()) for path in paths}
        guidance_check(actual == expected, "advice mutated retained inputs or created an assessment artifact")

    def trajectory(name, arguments):
        return {
            "workDir": str(root),
            "metrics": {"toolCallCount": 1},
            "events": [
                {"type": "tool_call", "data": {"toolCallId": "1", "toolName": name, "arguments": arguments}},
                {"type": "tool_result", "data": {"toolCallId": "1", "result": "control"}},
            ],
        }

    try:
        for topic in ("documentation-testing", "data-transfer"):
            directory = root / topic
            directory.mkdir()
            context = directory / "context.json"
            original = json.dumps({"topic": topic, "assessment": None, "findings": [],
                                   "execution_authorized": False}, separators=(",", ":")).encode()
            context.write_bytes(original)
            expected = {"context.json": sha256_bytes(original)}
            check(lambda: unchanged(directory, expected))
            check(lambda: check_guidance_trajectory(trajectory("Read", {"path": str(context)}), "factual"))
            for name in ("component.assessment.json", "findings.json", "decision-guidance.md", "execution-permission.json"):
                extra = directory / name
                extra.write_text("{}")
                check(lambda: unchanged(directory, expected), reject=True)
                extra.unlink()
            context.write_bytes(original.replace(b'"findings":[]', b'"findings":["new gap"]'))
            check(lambda: unchanged(directory, expected), reject=True)
            context.write_bytes(original)
            for name, arguments in (
                ("bash", {"command": "dotnet build"}),
                ("Bash", {"command": "npm install"}),
                ("execute", {"command": "dotnet trace collect"}),
                ("web_search", {"query": "new research"}),
                ("write_file", {"path": str(context), "content": "{}"}),
            ):
                check(lambda: check_guidance_trajectory(trajectory(name, arguments), "factual"), reject=True)
            unchanged(directory, expected)
        print(f"VALID requested help controls {count}")
    finally:
        shutil.rmtree(root)


def main():
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(required=True)

    command = sub.add_parser("requested-help-selftests")
    command.add_argument("--scratch", required=True)
    command.set_defaults(func=requested_help_selftests)

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
    command.add_argument("--kind", choices=["package", "component"], required=True)
    command.add_argument("--rows", type=int, required=True)
    command.set_defaults(func=verify_revision)

    command = sub.add_parser("prepare-embedded-sbom")
    command.add_argument("--snapshot", required=True)
    command.add_argument("--case", choices=["release-01", "release-02", "release-03"], required=True)
    command.add_argument("--root", required=True)
    command.set_defaults(func=prepare_embedded_sbom)

    command = sub.add_parser("embedded-sbom-selftests")
    command.add_argument("--snapshot", required=True)
    command.add_argument("--eval", required=True)
    command.add_argument("--scratch", required=True)
    command.add_argument("--controls", required=True)
    command.set_defaults(func=embedded_sbom_selftests)

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
            command.add_argument("--grading-files", action="store_true")
        else:
            command.add_argument("--mode", choices=["factual", "recommendations"], required=True)
            command.add_argument("--snapshot-sha256", required=True)
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
