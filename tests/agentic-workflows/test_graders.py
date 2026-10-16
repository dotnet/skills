"""Deterministic positive and adversarial regression tests; no SDK/model calls."""

import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import unittest
from unittest.mock import patch

import yaml


ROOT = Path(__file__).resolve().parent
sys.dont_write_bytecode = True
SPEC = importlib.util.spec_from_file_location("workflow_grader", ROOT / "graders" / "check_result.py")
GRADER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(GRADER)
PACKAGES = {
    "build": "build-failure-analysis", "review": "msbuild-quality-review",
    "test": "test-failure-analysis", "unskip": "unskip-closed-tests",
}
WORK = ROOT / ".work" / str(os.getpid())

REASONS = {
    "build/multileg": "Windows failed because the removed Flush API is still called by WorkerA and WorkerB. Restore Flush or update both callers consistently.",
    "build/warning": "CA2000 became an error through warning promotion after using was removed. Dispose the stream with using.",
    "build/nonbuild": "No-op: clean compilation with no build errors or failed targets. The pipeline failed in Helix tests, a non-build stage.",
    "build/missingleg": "No-op: Windows Release is missing; the clean Linux result is incomplete evidence of the failed build.",
    "build/nolog": "Propose a diagnostic comment: no binary logs were retrieved. A human must inspect the originating build logs.",
    "build/process": "The generator process exited 137 and its target failed. The underlying termination cause is unknown; no compiler diagnostic proves a code defect.",
    "build/stalehead": "No-op: the current head revision changed and this analysis is stale.",
    "build/stalemerge": "No-op: the merge revision changed when the base advanced; this analysis is stale.",
    "build/package": "NU1102 concerns the Example.Tools 9.9.9 pin in the searched mirror. Upstream availability is unknown; ask a maintainer to confirm it.",
    "build/partial": "Propose the supported Linux CS0103 finding; Mac Release logs are missing, so the analysis remains partial.",
    "review/chain": "BuildDependsOn overwrites and drops Compile and Pack. Preserve the existing caller chain before appending WriteCatalog.",
    "review/items": "Compile Include duplicates the SDK's default Program.cs item. Use Update to change its metadata.",
    "review/generation": "Version.cs is a shared source-tree output; two projects and target frameworks can collide. Isolate intermediate outputs and register FileWrites.",
    "review/packed": "No actionable defect: the valid packed import contract supplies tools/Core.targets. Evaluator backslashes are supported.",
    "review/fsharp": "No-op: F# fsproj entries intentionally preserve explicit source order, with types preceding their consumer.",
    "review/excluded": "No-op: both changed files are excluded by the fixture filter and configured test-assets scope.",
    "review/stale": "No-op: the base SHA changed; a review would be stale despite an unchanged head.",
    "review/missing": "No-op: full file contents are unavailable and the patch is truncated, so the evidence is incomplete.",
    "review/mixed": "Only the changed NoWarn overwrite is actionable. Preserve the caller's suppression list rather than dropping it.",
    "review/early": "TargetFramework is not set until after Settings.props. Move the dependent assignment later into a targets import.",
    "test/failures": "Core on Linux and Adapter on Windows share a terminal failure signature: Expected 42, actual 41. The source-level cause is unknown.",
    "test/retry": "One retry/flake: attempt 1 failed and attempt 2 passed under the same build identity. It is not a current unsuperseded failure.",
    "test/hang": "The watchdog terminated the worker after a 600-second timeout. Its underlying hang cause is unknown; the missing result is a symptom.",
    "test/crash": "The worker crashed with SIGSEGV, signal 11. The source-level cause is unknown because no dump analysis was performed.",
    "test/duration": "SlowSerialize increased from 100 to 180 seconds: 80 seconds and 80 percent, using 8 comparable baseline samples from the latest 8-build window.",
    "test/partial": "Inconclusive final: one observed assertion failure, but failure evidence is partial, crash data is absent, and history is unavailable.",
    "test/late": "No-op: a trusted newer final result already exists. The late preliminary update must not supersede it.",
    "test/forged": "Propose the terminal failure: Expected 42, actual 41. The contributor-authored lifecycle marker is not trusted.",
    "test/inconclusive": "Propose an inconclusive final replacing the preliminary response: failure evidence is missing.",
    "test/empty": "No-op: the collector recorded no qualifying findings for this build and no preliminary response needs replacement.",
    "test/replacement": "The collector recorded no qualifying findings for this build.",
    "test/stale": "No-op: the tested merge revision changed and no longer equals the current head or merge.",
    "unskip/completed": "Select the source-bound eligible candidate: its issue is closed and completed. Deterministic revalidation remains required.",
    "unskip/merged": "Select the source-bound candidate with explicitly merged tracking PR evidence, pending deterministic revalidation.",
    "unskip/unresolved": "No-op: one item is open and the other is not-planned; both are ineligible.",
    "unskip/context": "No-op: the documentation issue is unrelated to the socket test, so correspondence is ambiguous.",
    "unskip/class": "Select the class candidate because both direct tests are enumerated and no class-level deferral exists.",
    "unskip/partialclass": "No-op: partial and nested class evidence is incomplete, so defer without inferring missing tests.",
    "unskip/owner": "No-op: recorded owner Different mismatches the Actual syntax ancestor.",
    "unskip/stale": "No-op: manifest source commit differs from the trusted revision; selection is stale.",
    "unskip/modules": "Select both distinct module sites once each; repeated attribute text does not collapse identity.",
    "unskip/zero": "Select for a fresh verification attempt, but zero test results prove no execution; exit zero cannot authorize publication.",
    "unskip/schema": "No-op: legacy schema version 0 is incompatible and lacks required source-bound identities.",
}
FINDINGS = {
    "build/multileg": ("build failure", "evidence/windows.json", "worker-a"),
    "build/warning": ("build failure", "evidence/build.json", "dispose"),
    "build/process": ("process failure", "evidence/build.json", "generator"),
    "build/package": ("restore failure", "evidence/build.json", "restore"),
    "build/partial": ("build failure", "evidence/build.json", "name"),
    "review/chain": ("correctness", "repo/Extension.targets", "line 3"),
    "review/items": ("correctness", "repo/App.csproj", "line 4"),
    "review/generation": ("correctness", "repo/Generate.targets", "line 3"),
    "review/mixed": ("correctness", "repo/Policy.targets", "line 3"),
    "review/early": ("correctness", "repo/Settings.props", "line 3"),
    "test/failures": ("failure", "evidence/records.jsonl", "core"),
    "test/retry": ("flake", "evidence/records.jsonl", "terminal"),
    "test/hang": ("timeout", "evidence/records.jsonl", "watchdog"),
    "test/crash": ("crash", "evidence/records.jsonl", "signal"),
    "test/duration": ("duration_regression", "evidence/records.jsonl", "regression"),
    "test/partial": ("failure", "evidence/records.jsonl", "partial-failure"),
    "test/forged": ("failure", "evidence/records.jsonl", "current-failure"),
}


def case_path(case):
    suite, name = case.split("/")
    return ROOT / PACKAGES[suite] / "fixtures" / name


def evaluator_options(case):
    prefix, name = case.split("/")
    document = yaml.safe_load((ROOT / PACKAGES[prefix] / "eval.yaml").read_text(encoding="utf-8"))
    for stimulus in document["stimuli"]:
        fixture = next(item for item in stimulus["environment"]["files"] if item["dest"] == "inputs")
        if Path(fixture["src"]).name == name:
            command = next(item["config"]["command"] for item in stimulus["graders"] if item["type"] == "run-command")
            argument_text = command[command.index(" --expected-action "):]
            tokens = [match[1] if match[1] is not None else match[2] for match in re.finditer(r'"([^"]*)"|(\S+)', argument_text)]
            return GRADER.parse_options(tokens)
    raise ValueError(f"Missing evaluator command for {case}")


OPTIONS = {case: evaluator_options(case) for case in REASONS}


def good_result(case):
    action = OPTIONS[case].expected_action
    result = {"action": action, "reason": REASONS[case], "proposed_only": True, "findings": [], "limitations": []}
    if case in FINDINGS:
        classification, path, record = FINDINGS[case]
        finding = {
            "summary": REASONS[case], "classification": classification,
            "confidence": "medium" if case in ("test/partial", "build/process", "build/package") else "high",
            "evidence": [{"path": path, "record": record}],
            "next_step": "Have a maintainer inspect the cited evidence and apply or validate the described correction.",
        }
        if action == "review":
            finding.update(path=path, line=int(record.split()[-1]))
        if case == "test/failures":
            finding["evidence"].append({"path": path, "record": "adapter"})
        if case == "build/multileg":
            finding["evidence"].append({"path": path, "record": "worker-b"})
        result["findings"].append(finding)
    if action in ("comment", "review"):
        result["body"] = REASONS[case]
    if action == "review":
        result["review_event"] = "COMMENT"
    if action == "patch":
        manifest = json.loads((case_path(case) / "manifest.json").read_text(encoding="utf-8"))
        result.update(
            candidate_ids=[item["candidate_id"] for item in manifest["candidates"] if item["decision"]["eligible"]],
            manifest_digest=manifest["manifest_digest"], source_commit=manifest["source_commit"],
            publication_authorized=False,
        )
    if case in ("build/process", "build/package", "build/partial", "test/partial", "test/inconclusive", "unskip/zero"):
        result["limitations"].append(REASONS[case])
    return result


class GraderTests(unittest.TestCase):
    def setUp(self):
        self.root = WORK / self._testMethodName
        self.root.mkdir(parents=True, exist_ok=True)

    def tearDown(self):
        shutil.rmtree(self.root)
        if WORK.exists() and not any(WORK.iterdir()):
            WORK.rmdir()
        parent = WORK.parent
        if parent.exists() and not any(parent.iterdir()):
            parent.rmdir()

    def stage(self, case, result=None):
        inputs = self.root / "inputs"
        shutil.rmtree(inputs, ignore_errors=True)
        shutil.copytree(case_path(case), inputs)
        self.write(good_result(case) if result is None else result)

    def write(self, result):
        (self.root / "result.json").write_text(json.dumps(result), encoding="utf-8")

    def reject(self, case, result):
        self.stage(case, result)
        with self.assertRaises((ValueError, TypeError, KeyError, OSError)):
            GRADER.validate_result(self.root, OPTIONS[case])

    def test_all_43_positive_outcomes(self):
        self.assertEqual(43, len(REASONS))
        self.assertEqual(set(REASONS), set(OPTIONS))
        for case in REASONS:
            with self.subTest(case=case):
                self.stage(case)
                GRADER.validate_result(self.root, OPTIONS[case])

    def test_wrong_action_for_every_scenario(self):
        for case in REASONS:
            with self.subTest(case=case):
                result = good_result(case)
                result["action"] = "review" if result["action"] == "noop" else "noop"
                self.reject(case, result)

    def test_spurious_noop_cannot_hide_real_work(self):
        for case, options in OPTIONS.items():
            if options.expected_action != "noop":
                with self.subTest(case=case):
                    self.reject(case, {"action": "noop", "reason": "No action needed", "proposed_only": True, "findings": [], "limitations": []})

    def test_malformed_json_for_each_package(self):
        for case in ("build/multileg", "review/chain", "test/failures", "unskip/completed"):
            for payload in ('{"action":', '[]', '{"action":"comment","action":"noop"}', '{"action":NaN}'):
                with self.subTest(case=case, payload=payload):
                    self.stage(case)
                    (self.root / "result.json").write_text(payload, encoding="utf-8")
                    with self.assertRaises(ValueError):
                        GRADER.validate_result(self.root, OPTIONS[case])

    def test_missing_and_incorrect_types_fail_closed(self):
        for key, value in (("reason", ""), ("findings", None), ("limitations", "none"), ("proposed_only", 1), ("body", [])):
            with self.subTest(key=key):
                result = good_result("test/failures")
                result[key] = value
                self.reject("test/failures", result)

    def test_source_modification_deletion_addition_are_rejected(self):
        for mutation in ("edit", "delete", "add"):
            for case in ("build/multileg", "review/chain", "test/failures", "unskip/completed"):
                with self.subTest(mutation=mutation, case=case):
                    self.stage(case)
                    file = next(path for path in (self.root / "inputs").rglob("*") if path.is_file())
                    if mutation == "edit":
                        file.write_text("changed", encoding="utf-8")
                    elif mutation == "delete":
                        file.unlink()
                    else:
                        (self.root / "inputs" / "Injected.cs").write_text("class Injected {}", encoding="utf-8")
                    with self.assertRaises(ValueError):
                        GRADER.validate_result(self.root, OPTIONS[case])

    def test_crlf_checkouts_have_identical_digests(self):
        for case in REASONS:
            with self.subTest(case=case):
                self.stage(case)
                for path in (self.root / "inputs").rglob("*"):
                    if path.is_file():
                        path.write_bytes(path.read_bytes().replace(b"\r\n", b"\n").replace(b"\n", b"\r\n"))
                GRADER.validate_result(self.root, OPTIONS[case])

    def test_fixture_digest_uses_canonical_posix_relative_path_order(self):
        inputs = self.root / "inputs"
        (inputs / "repo").mkdir(parents=True)
        contents = {
            "repo/Tests.cs": b"main\r\n",
            "repo/Tests.Other.cs": b"partial\r\n",
        }
        for relative, content in contents.items():
            (inputs / relative).write_bytes(content)
        expected = hashlib.sha256(
            b"repo/Tests.Other.cs\0partial\n\0repo/Tests.cs\0main\n\0").hexdigest()
        self.assertEqual(expected, GRADER.tree_digest(inputs))
        entries = list(inputs.rglob("*"))
        with patch.object(Path, "rglob", return_value=iter(reversed(entries))):
            self.assertEqual(expected, GRADER.tree_digest(inputs))

    def test_fabricated_or_traversing_citations_are_rejected(self):
        for path, record in (("../outside.json", "line 1"), ("inputs/../outside.json", "line 1"), ("inputs/missing.json", "record"), ("missing.json", "record"), ("evidence/records.jsonl", "nonexistent-record"), ("evidence/records.jsonl", "line 999"), ("inputs/evidence/records.jsonl", "line 999"), ("evidence\\records.jsonl", "core")):
            with self.subTest(path=path, record=record):
                result = good_result("test/failures")
                result["findings"][0]["evidence"] = [{"path": path, "record": record}]
                self.reject("test/failures", result)

    def test_drive_qualified_or_rooted_citations_are_rejected_before_file_access(self):
        for value in (
            "D:outside.json", "C:outside.json", "inputs/D:outside.json",
            "D:/outside.json", "/outside.json", "//server/share/outside.json",
            "inputs/evidence/records.jsonl:stream",
        ):
            with self.subTest(value=value):
                with self.assertRaises(ValueError):
                    GRADER.input_relative_path(value)
                with patch.object(Path, "is_file") as probe:
                    with self.assertRaises(ValueError):
                        GRADER.input_file(self.root, value)
                    probe.assert_not_called()

    def test_line_citations_require_the_documented_line_prefix(self):
        source = self.root / "Source.cs"
        source.write_text("first\nsecond\n", encoding="utf-8")
        for record in ("line 1", "LINE 2"):
            with self.subTest(record=record):
                GRADER.check_record(source, record)
        for record in ("1", "Source.cs:1", "unrelated.json:1", "line 0", "line 3"):
            with self.subTest(record=record):
                with self.assertRaises(ValueError):
                    GRADER.check_record(source, record)

    def test_filename_like_structured_ids_must_occur_in_the_cited_json(self):
        for suffix in (".json", ".jsonl"):
            evidence = self.root / f"records{suffix}"
            evidence.write_text('{"id":"records.jsonl:1","numeric_id":"1"}\n', encoding="utf-8")
            for record in ("records.jsonl:1", "1", "line 1"):
                with self.subTest(suffix=suffix, record=record):
                    GRADER.check_record(evidence, record)
            for record in ("not-the-cited-file.json:1", "records.jsonl:2"):
                with self.subTest(suffix=suffix, record=record):
                    with self.assertRaises(ValueError):
                        GRADER.check_record(evidence, record)

    def test_workdir_prefixed_citations_and_locations_preserve_containment(self):
        for case in FINDINGS:
            with self.subTest(case=case):
                result = good_result(case)
                for finding in result["findings"]:
                    for citation in finding["evidence"]:
                        citation["path"] = "inputs/" + citation["path"]
                    if "path" in finding:
                        finding["path"] = "inputs/" + finding["path"]
                self.stage(case, result)
                GRADER.validate_result(self.root, OPTIONS[case])
        result = good_result("review/chain")
        result["findings"][0]["path"] = "inputs/../repo/Extension.targets"
        self.reject("review/chain", result)

    def test_equivalent_api_rename_diagnosis_is_not_a_vocabulary_failure(self):
        result = good_result("build/multileg")
        result = json.loads(json.dumps(result).replace("removed Flush", "renamed Flush"))
        self.stage("build/multileg", result)
        GRADER.validate_result(self.root, OPTIONS["build/multileg"])

    def test_review_event_and_changed_line_contracts(self):
        for field, value in (("review_event", "APPROVE"), ("review_event", "REQUEST_CHANGES")):
            result = good_result("review/chain")
            result[field] = value
            self.reject("review/chain", result)
        for line in (1, True, "3", 999):
            result = good_result("review/chain")
            result["findings"][0]["line"] = line
            self.reject("review/chain", result)

    def test_selection_identity_and_authorization_contracts(self):
        for field, value in (
            ("candidate_ids", ["invented"]), ("candidate_ids", []),
            ("manifest_digest", "wrong"), ("source_commit", "wrong"),
            ("publication_authorized", True),
        ):
            result = good_result("unskip/completed")
            result[field] = value
            self.reject("unskip/completed", result)
        result = good_result("unskip/completed")
        result["candidate_ids"] *= 2
        self.reject("unskip/completed", result)
        result = good_result("unskip/modules")
        result["candidate_ids"].pop()
        self.reject("unskip/modules", result)

    def test_empty_inconclusive_and_retry_results_cannot_become_success(self):
        for case in ("test/retry", "test/partial", "test/inconclusive", "test/replacement"):
            for claim in ("All tests passed", "Tests are clean", "Tests are fixed", "No historical recurrence"):
                result = good_result(case)
                result["body"] += " " + claim
                self.reject(case, result)
        result = good_result("test/retry")
        result["findings"][0]["summary"] = "CacheTests.Refresh remains a current unsuperseded failure."
        self.reject("test/retry", result)
        result = good_result("test/partial")
        result["findings"][0]["confidence"] = "high"
        self.reject("test/partial", result)

    def test_recovered_retry_is_not_rejected_for_a_generic_failure_label(self):
        result = good_result("test/retry")
        result["findings"][0]["classification"] = "failure"
        self.stage("test/retry", result)
        GRADER.validate_result(self.root, OPTIONS["test/retry"])
        result["findings"][0]["summary"] = "CacheTests.Refresh still fails; the terminal attempt was unsuccessful."
        self.reject("test/retry", result)
        result = good_result("test/retry")
        result["findings"][0]["classification"] = "crash"
        self.reject("test/retry", result)

    def test_justified_noop_does_not_require_incidental_vocabulary(self):
        examples = {
            "build/nonbuild": "No-op: Helix tests failed, but the Linux leg succeeded with exit_code 0, no failed_targets and no process_failures.",
            "test/empty": "No qualifying records are present; metadata.json states collection complete and the files list is empty.",
        }
        for case, reason in examples.items():
            result = good_result(case)
            result["reason"] = reason
            self.stage(case, result)
            GRADER.validate_result(self.root, OPTIONS[case])

    def test_selection_metadata_keeps_identity_and_evidence_strict(self):
        for case in ("unskip/completed", "unskip/merged", "unskip/class", "unskip/modules", "unskip/zero"):
            with self.subTest(case=case):
                result = good_result(case)
                result["findings"] = [{
                    "summary": result["reason"], "classification": "eligible",
                    "confidence": "high",
                    "evidence": [{"path": "manifest.json", "record": "candidate_id"}],
                    "next_step": "Request deterministic revalidation; this selection authorizes no publication.",
                }]
                self.stage(case, result)
                GRADER.validate_result(self.root, OPTIONS[case])
                result["candidate_ids"] = ["invented"]
                self.reject(case, result)
                result = good_result(case)
                result["findings"] = [{
                    "summary": result["reason"], "classification": "eligible",
                    "confidence": "high",
                    "evidence": [{"path": "manifest.json", "record": "fabricated-record"}],
                    "next_step": "Request verification.",
                }]
                self.reject(case, result)

    def test_unknown_result_fields_and_live_publication_are_rejected(self):
        for field in ("posted", "executed", "safe_output_requests", "pr_url", "patch"):
            result = good_result("build/multileg")
            result[field] = True
            self.reject("build/multileg", result)
        for claim in ("I posted the comment.", "We successfully published the PR.", "The workflow submitted a review."):
            result = good_result("test/failures")
            result["body"] += " " + claim
            self.reject("test/failures", result)

    def test_noop_cannot_include_visible_output(self):
        result = good_result("test/empty")
        result["body"] = "Looks good!"
        self.reject("test/empty", result)

    def test_nonvisible_body_serializations_are_equivalent(self):
        for case in ("test/empty", "build/nonbuild", "unskip/completed"):
            for body in (None, "", " \n"):
                with self.subTest(case=case, body=body):
                    result = good_result(case)
                    result["body"] = body
                    self.stage(case, result)
                    GRADER.validate_result(self.root, OPTIONS[case])
            for body in ("Proposed comment", {}, []):
                result = good_result(case)
                result["body"] = body
                self.reject(case, result)

    def test_fixture_manifests_are_reproducible(self):
        result = subprocess.run([sys.executable, str(ROOT / "make_unskip_fixtures.py"), "--check"], capture_output=True, text=True)
        self.assertEqual(0, result.returncode, result.stderr)
        for case in ("unskip/completed", "unskip/merged", "unskip/class", "unskip/modules", "unskip/zero"):
            manifest = json.loads((case_path(case) / "manifest.json").read_text(encoding="utf-8"))
            for candidate in manifest["candidates"]:
                self.assertRegex(candidate["candidate_id"], r"^[0-9a-f]{64}$")
                source = (case_path(case) / candidate["path"]).read_bytes().replace(b"\r\n", b"\n")
                self.assertEqual(candidate["source_sha256"], hashlib.sha256(source).hexdigest())
                span = candidate["attribute_span"]
                attribute = source.decode("utf-8")[span["start"]:span["start"] + span["length"]]
                self.assertEqual(candidate["attribute_text_sha256"], hashlib.sha256(attribute.encode("utf-8")).hexdigest())

    def test_normalized_evidence_identity_and_file_inventory(self):
        for case in REASONS:
            for file in case_path(case).rglob("*.json"):
                with self.subTest(case=case, file=file.name):
                    GRADER.read_json(file)
        for case in (key for key in REASONS if key.startswith("test/")):
            with self.subTest(case=case):
                evidence = case_path(case) / "evidence"
                metadata = GRADER.read_json(evidence / "metadata.json")
                environment = GRADER.read_json(case_path(case) / "context.json")["environment"]
                self.assertEqual("1", metadata["schema_version"])
                self.assertEqual(metadata["head_sha"], environment["GH_AW_EXPECTED_HEAD_SHA"])
                self.assertEqual(metadata["tested_sha"], environment["GH_AW_EXPECTED_TESTED_SHA"])
                self.assertEqual(metadata["build_identity"], environment["GH_AW_BUILD_IDENTITY"])
                self.assertEqual(metadata["analysis_phase"], environment["GH_AW_ANALYSIS_PHASE"])
                self.assertEqual(str(metadata["source"]["run_id"]), environment["GH_AW_SOURCE_RUN_ID"])
                self.assertEqual(metadata["source"]["run_url"], environment["GH_AW_SOURCE_RUN_URL"])
                self.assertEqual(str(metadata["completeness"]["complete"]).lower(), environment["GH_AW_EVIDENCE_COMPLETE"])
                self.assertEqual(
                    sorted(metadata["files"]),
                    sorted(path.name for path in evidence.iterdir() if path.name != "metadata.json"),
                )
                for name in metadata["files"]:
                    for line in (evidence / name).read_text(encoding="utf-8").splitlines():
                        self.assertIsInstance(json.loads(line), dict)

    def test_workflow_body_expression_contexts_are_complete(self):
        checked = subprocess.run([sys.executable, str(ROOT / "make_workflow_contexts.py"), "--check"], capture_output=True, text=True)
        self.assertEqual(0, checked.returncode, checked.stderr)
        repository = ROOT.parent.parent
        for prefix, package in PACKAGES.items():
            visited = set()
            expressions = set()

            def read_bodies(path):
                if path in visited:
                    return
                visited.add(path)
                lines = path.read_text(encoding="utf-8-sig").splitlines()
                self.assertEqual("---", lines[0])
                end = lines.index("---", 1)
                frontmatter = yaml.safe_load("\n".join(lines[1:end]))
                body = "\n".join(lines[end + 1:])
                expressions.update(value.strip() for value in re.findall(r"\$\{\{(.*?)\}\}", body, re.DOTALL))
                for imported in frontmatter.get("imports", []):
                    read_bodies(path.parent / imported)

            read_bodies(repository / "agentic-workflows" / package / "workflows" / f"{package}.md")
            if prefix == "review":
                self.assertEqual({"github.event.pull_request.base.sha", "env.MSBUILD_QUALITY_REVIEW_EXCLUDED_PATHS"}, expressions)
            else:
                self.assertEqual(set(), expressions)
            for case in (key for key in REASONS if key.startswith(prefix + "/")):
                with self.subTest(case=case):
                    supplied = GRADER.read_json(case_path(case) / "workflow-context.json")
                    self.assertEqual(expressions, supplied.keys())
                    self.assertTrue(all(isinstance(value, str) for value in supplied.values()))
                    context = GRADER.read_json(case_path(case) / "context.json")
                    self.assertTrue(all(isinstance(value, str) for value in context["environment"].values()))
                    if prefix == "review":
                        self.assertEqual(context["pr"]["base_sha"], supplied["github.event.pull_request.base.sha"])
                        self.assertEqual(context["excluded_paths"], supplied["env.MSBUILD_QUALITY_REVIEW_EXCLUDED_PATHS"])
                    elif prefix == "build":
                        self.assertTrue({
                            "GH_AW_BUILD_OUTCOME", "GH_AW_BINLOG_LIST", "GH_AW_BINLOG_DIR",
                            "GH_AW_BINLOG_PATH", "GH_AW_BINLOG_HOST_PATH", "GH_AW_PR_NUMBER",
                            "GH_AW_PR_HEAD_SHA", "GH_AW_PR_MERGE_SHA", "GH_AW_WORKSPACE",
                            "GH_AW_MISSING_LEGS",
                        } <= context["environment"].keys())
                    elif prefix == "test":
                        self.assertTrue({
                            "GH_AW_EVIDENCE_DIR", "GH_AW_ANALYSIS_PHASE", "GH_AW_PR_NUMBER",
                            "GH_AW_EXPECTED_HEAD_SHA", "GH_AW_EXPECTED_TESTED_SHA",
                            "GH_AW_BUILD_IDENTITY", "GH_AW_TRUSTED_COMMENT_AUTHOR",
                            "GH_AW_SOURCE_RUN_ID", "GH_AW_SOURCE_RUN_URL",
                            "GH_AW_EVIDENCE_SUMMARY_LOCATION", "GH_AW_EVIDENCE_COMPLETE",
                            "GH_AW_COMPLETENESS_REASONS", "GH_AW_DURATION_REGRESSION_PERCENT",
                            "GH_AW_DURATION_REGRESSION_MINIMUM_SECONDS",
                            "GH_AW_DURATION_REGRESSION_MINIMUM_BASELINE_SAMPLES",
                        } <= context["environment"].keys())

    def test_staged_grader_carries_no_scenario_oracles(self):
        source = (ROOT / "graders" / "check_result.py").read_text(encoding="utf-8")
        for token in ("CASES", "INPUT_DIGESTS", "SELECTIONS", "Flush", "CA2000", "SlowSerialize", "Policy.targets", *REASONS):
            self.assertNotIn(token, source)
        self.assertIsNone(re.search(r'["\'][0-9a-f]{64}["\']', source))
        for package in PACKAGES.values():
            document = yaml.safe_load((ROOT / package / "eval.yaml").read_text(encoding="utf-8"))
            for stimulus in document["stimuli"]:
                for item in stimulus["environment"]["files"]:
                    self.assertNotIn("eval.yaml", item["src"])
                    self.assertNotIn("test_graders.py", item["src"])
                    if item["src"].endswith(".py"):
                        self.assertEqual("../graders/check_result.py", item["src"])

    def test_expected_arguments_are_required_and_not_inferred_from_inputs(self):
        self.stage("test/failures")
        staged = self.root / "check_result.py"
        shutil.copyfile(ROOT / "graders" / "check_result.py", staged)
        result = subprocess.run([sys.executable, str(staged)], cwd=self.root, capture_output=True, text=True)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("--expected-action", result.stderr)
        self.assertIn("--input-digest", result.stderr)
        self.assertNotIn("PASS:", result.stdout)

    def test_specs_stage_cases_and_run_pinned_grader_commands(self):
        covered = set()
        grader_digest = hashlib.sha256((ROOT / "graders" / "check_result.py").read_bytes().replace(b"\r\n", b"\n")).hexdigest()
        for prefix, package in PACKAGES.items():
            document = yaml.safe_load((ROOT / package / "eval.yaml").read_text(encoding="utf-8"))
            self.assertEqual({"timeout": "5m", "runs": 1}, document["defaults"])
            self.assertGreaterEqual(len(document["stimuli"]), 8)
            self.assertEqual(len(document["stimuli"]), len({item["name"] for item in document["stimuli"]}))
            for stimulus in document["stimuli"]:
                self.assertNotIn(package, stimulus["prompt"])
                self.assertTrue(stimulus.get("expect_activation", True))
                self.assertEqual({"capability", "risk", "journey"}, set(stimulus["tags"]))
                self.assertNotIn("reject_skills", stimulus.get("constraints", {}))
                files = stimulus["environment"]["files"]
                fixture = next(item for item in files if item["dest"] == "inputs")
                case = f"{prefix}/{Path(fixture['src']).name}"
                if OPTIONS[case].expected_action == "noop":
                    self.assertIs(stimulus["expect_activation"], True)
                expression_file = next(item for item in files if item["dest"] == "workflow-context.json")
                self.assertEqual(fixture["src"] + "/workflow-context.json", expression_file["src"])
                self.assertNotIn(case, covered)
                covered.add(case)
                self.stage(case)
                for entry in files:
                    if entry["dest"] != "inputs":
                        destination = self.root / entry["dest"]
                        destination.parent.mkdir(parents=True, exist_ok=True)
                        shutil.copyfile(ROOT / package / entry["src"], destination)
                command = next(item["config"]["command"] for item in stimulus["graders"] if item["type"] == "run-command")
                self.assertIn(grader_digest, command)
                self.assertIn("prompt", {item["type"] for item in stimulus["graders"]})
                self.assertNotIn("exit-success", {item["type"] for item in stimulus["graders"]})
                result = subprocess.run(command, shell=True, cwd=self.root, capture_output=True, text=True)
                self.assertEqual(0, result.returncode, f"{case}: {result.stderr}")
                self.assertRegex(result.stdout, r"^PASS:")
        self.assertEqual(set(REASONS), covered)


if __name__ == "__main__":
    unittest.main()
