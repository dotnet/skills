"""Replay calibration goldens and reject misleading behavioral evidence."""

import copy
import base64
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import unittest

import yaml


ROOT = Path(__file__).resolve().parents[3]
SUITE = Path(__file__).resolve().parent
SPEC = yaml.safe_load((SUITE / "eval.yaml").read_text(encoding="utf-8"))
checker_spec = importlib.util.spec_from_file_location(
    "eval_quality", ROOT / "eng" / "eval-quality" / "check_eval_quality.py"
)
checker = importlib.util.module_from_spec(checker_spec)
checker_spec.loader.exec_module(checker)


def golden_document(stimulus):
    reference = stimulus["golden_trajectory"]
    if "inline" in reference:
        return reference["inline"]
    return json.loads((SUITE / reference["path"]).read_text(encoding="utf-8"))


class EvidenceCalibration(unittest.TestCase):
    def materialize(self):
        directory = tempfile.TemporaryDirectory(prefix=".calibration-", dir=SUITE)
        self.addCleanup(directory.cleanup)
        root = Path(directory.name)
        for item in SPEC["stimuli"][0]["environment"]["files"]:
            (root / item["dest"]).parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(SUITE / item["src"], root / item["dest"])
        return root

    def run_command(self, root, config):
        command = config["command"]
        if "args" in config:
            command = [command, *config["args"]]
        return subprocess.run(
            command, cwd=root, shell="args" not in config, capture_output=True,
            text=True, timeout=30,
        )

    def golden_workspace(self):
        root = self.materialize()
        self.apply_golden(root)
        return root

    def apply_golden(self, root):
        applied = subprocess.run(
            ["git", "apply", "--directory", root.relative_to(ROOT).as_posix(),
             str(SUITE / "golden.patch")], cwd=ROOT,
            capture_output=True, text=True, timeout=30,
        )
        self.assertEqual(0, applied.returncode, applied.stderr)

    def workspace_grader(self):
        return next(item["config"] for item in SPEC["stimuli"][0]["graders"]
                    if item["type"] == "run-command")

    def response_errors(self, stimulus, response):
        checker.errors.clear()
        document = copy.deepcopy(golden_document(stimulus))
        document["steps"][-1]["message"] = response
        checker.check_trajectory_output_graders(
            str(SUITE / "eval.yaml"), stimulus, document, "calibration"
        )
        return list(checker.errors)

    def test_every_golden_response_passes_output_graders(self):
        goldens = [item for item in SPEC["stimuli"] if "golden_trajectory" in item]
        self.assertEqual(12, len(goldens))
        for stimulus in goldens:
            with self.subTest(stimulus=stimulus["name"]):
                response = golden_document(stimulus)["steps"][-1]["message"]
                self.assertEqual([], self.response_errors(stimulus, response))

    def test_misleading_evidence_is_rejected(self):
        defects = [
            ("test_exact_threshold", "unnamed_test"),
            ("buildable no-op", "deleted symbol"),
            ("200 from head bbbbbbb's contractual 2000", "the same value"),
            ("both revisions", "the new version"),
            ("independent contract's\n8", "implementation's current value"),
            ("unverified", "executed: detected"),
            ("unverified", "release proven"),
            ("equivalent", "survived"),
            ("static: predicts detection", "executed: detected"),
        ]
        for stimulus, (original, replacement) in zip(
            SPEC["stimuli"][:9], defects, strict=True
        ):
            with self.subTest(stimulus=stimulus["name"]):
                response = golden_document(stimulus)["steps"][-1]["message"]
                self.assertIn(original, response)
                broken = response.replace(original, replacement)
                if stimulus is SPEC["stimuli"][1]:
                    broken = (
                        "**Test-value evidence:** executed: detected. "
                        "CS1061 proves the behavior is protected when Export is deleted."
                    )
                self.assertTrue(self.response_errors(stimulus, broken))

    def test_actual_defect_and_restored_green_use_the_same_tests(self):
        root = self.materialize()
        test_bytes = (root / "test_boundary.py").read_bytes()
        original_bytes = (root / "boundary.py").read_bytes()
        command = {
            "command": "python -B -m unittest -v test_boundary.BoundaryTests 2>&1"
        }
        red = self.run_command(root, command)
        self.assertEqual(1, red.returncode, red.stdout + red.stderr)
        self.assertIn("test_exact_threshold", red.stdout)
        self.assertIn("AssertionError: 0 != 10", red.stdout)
        self.assertRegex(red.stdout, r"Ran 3 tests[\s\S]*FAILED \(failures=1\)")
        self.assertEqual(2, len(re.findall(r"\.\.\. ok", red.stdout)))
        self.assertNotIn("skipped", red.stdout)

        self.apply_golden(root)
        correct_bytes = (root / "boundary.py").read_bytes()
        green = self.run_command(root, command)
        self.assertEqual(0, green.returncode, green.stdout + green.stderr)
        self.assertRegex(green.stdout, r"Ran 3 tests[\s\S]*OK")

        (root / "boundary.py").write_bytes(original_bytes)
        repeated_red = self.run_command(root, command)
        self.assertEqual(1, repeated_red.returncode)
        self.assertIn("AssertionError: 0 != 10", repeated_red.stdout)
        (root / "boundary.py").write_bytes(correct_bytes)
        restored = self.run_command(root, command)
        self.assertEqual(0, restored.returncode, restored.stdout + restored.stderr)
        self.assertRegex(restored.stdout, r"Ran 3 tests[\s\S]*OK")
        self.assertEqual(test_bytes, (root / "test_boundary.py").read_bytes())
        self.assertFalse(list(root.glob("__pycache__")))

    def test_golden_workspace_passes_all_file_and_command_graders(self):
        root = self.golden_workspace()
        for grader in SPEC["stimuli"][0]["graders"]:
            config = grader.get("config", {})
            with self.subTest(grader=grader["type"], config=config):
                if grader["type"] == "file-contains":
                    self.assertIn(
                        config["value"],
                        (root / config["path"]).read_text(encoding="utf-8"),
                    )
                elif grader["type"] == "run-command":
                    result = self.run_command(root, config)
                    self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                    if "stdout_matches" in config:
                        self.assertRegex(result.stdout, config["stdout_matches"])

    def test_preservation_grader_rejects_changed_or_missing_tests(self):
        config = self.workspace_grader()
        for missing in (False, True):
            with self.subTest(missing=missing):
                root = self.golden_workspace()
                path = root / "test_boundary.py"
                if missing:
                    path.unlink()
                else:
                    path.write_bytes(path.read_bytes() + b"\n# unrelated change\n")
                self.assertNotEqual(0, self.run_command(root, config).returncode)

    def test_only_changed_product_may_omit_its_final_lf(self):
        root = self.golden_workspace()
        product = root / "boundary.py"
        tests = root / "test_boundary.py"
        test_bytes = tests.read_bytes()
        fixed_bytes = product.read_bytes()
        self.assertTrue(fixed_bytes.endswith(b"\n"))
        without_lf = fixed_bytes[:-1]
        product.write_bytes(without_lf)
        result = self.run_command(root, self.workspace_grader())
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        replay = json.loads(result.stdout)
        self.assertEqual(hashlib.sha256(without_lf).hexdigest(), replay["fixed_sha256"])
        self.assertEqual(without_lf, product.read_bytes())
        self.assertEqual(test_bytes, tests.read_bytes())
        for extra in (b"\n# unrelated comment\n", b" # unrelated comment",
                      b"\n\n", b" ", b"\r\n"):
            with self.subTest(extra=extra):
                product.write_bytes(without_lf + extra)
                rejected = self.run_command(root, self.workspace_grader())
                self.assertNotEqual(0, rejected.returncode, rejected.stdout + rejected.stderr)
        product.write_bytes(without_lf)
        tests.write_bytes(test_bytes[:-1])
        rejected = self.run_command(root, self.workspace_grader())
        self.assertNotEqual(0, rejected.returncode, rejected.stdout + rejected.stderr)

    def test_complete_scope_rejects_unrelated_and_evaluator_changes(self):
        mutations = [
            ("boundary.py", b"\n# unrelated production edit\n"),
            ("notes.txt", b"unexpected user file\n"),
            (".eval/notes.txt", b"unexpected evaluator-directory file\n"),
            (".eval/check_workspace.py", b"\nraise SystemExit(0)\n"),
            ("test-value-evidence/SKILL.md", b"not a harness-installed skill\n"),
        ]
        for name, extra in mutations:
            with self.subTest(path=name):
                root = self.golden_workspace()
                path = root / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes((path.read_bytes() if path.exists() else b"") + extra)
                result = self.run_command(root, self.workspace_grader())
                self.assertNotEqual(0, result.returncode, result.stdout + result.stderr)
        root = self.golden_workspace()
        (root / "unexpected-empty-directory").mkdir()
        self.assertNotEqual(0, self.run_command(root, self.workspace_grader()).returncode)

    def test_isolated_authentication_rejects_shadowed_standard_library(self):
        root = self.golden_workspace()
        config = self.workspace_grader()
        self.assertEqual("-I", config["args"][0])
        digest = re.search(r"hexdigest\(\) == '([a-f0-9]{64})'",
                           config["args"][config["args"].index("-c") + 1]).group(1)
        (root / "hashlib.py").write_text(
            "class ForgedDigest:\n"
            f"    def hexdigest(self): return '{digest}'\n"
            "def sha256(data): return ForgedDigest()\n",
            encoding="utf-8",
        )
        (root / ".eval" / "check_workspace.py").write_text(
            "print('{\"kind\": \"grader-replay-not-executor-history\"}')\n",
            encoding="utf-8",
        )
        legacy = copy.deepcopy(config)
        legacy["args"].remove("-I")
        bypass = self.run_command(root, legacy)
        self.assertEqual(0, bypass.returncode, bypass.stdout + bypass.stderr)
        self.assertRegex(bypass.stdout, config["stdout_matches"])
        rejected = self.run_command(root, config)
        self.assertNotEqual(0, rejected.returncode, rejected.stdout + rejected.stderr)

    def test_replay_emits_measured_pair_and_never_uses_stale_outputs(self):
        root = self.golden_workspace()
        before = {path.relative_to(root): path.read_bytes()
                  for path in root.rglob("*") if path.is_file()}
        for _ in range(2):
            result = self.run_command(root, self.workspace_grader())
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            artifact = json.loads(result.stdout)
            self.assertEqual("grader-replay-not-executor-history", artifact["kind"])
            red, green = artifact["runs"]
            self.assertEqual([1, 0], [red["exit"], green["exit"]])
            self.assertEqual(
                {"selected": 3, "executed": 3, "passed": 2, "failed": 1,
                 "errors": 0, "skipped": 0}, red["counts"])
            self.assertEqual({**red["counts"], "passed": 3, "failed": 0}, green["counts"])
            self.assertIn("AssertionError: 0 != 10", red["output"])
            self.assertEqual([["test_exact_threshold", "FAIL"]],
                             [case for case in red["cases"] if case[1] == "FAIL"])
            self.assertEqual(hashlib.sha256((SUITE / "fixtures" / "boundary.py")
                                           .read_bytes()).hexdigest(),
                             artifact["original_sha256"])
            self.assertEqual(before, {path.relative_to(root): path.read_bytes()
                                    for path in root.rglob("*") if path.is_file()})
        (root / ".eval" / "stale-red.log").write_text(
            red["output"], encoding="utf-8")
        self.assertNotEqual(0, self.run_command(root, self.workspace_grader()).returncode)

    def test_counterfactual_rejects_green_skipped_wrong_assertion_and_zero_selection(self):
        correct = (SUITE / "fixtures" / "boundary.py").read_bytes().replace(
            b"amount > 100", b"amount >= 100")
        alternatives = [
            correct + b"\n# equivalent green counterfactual\n",
            b"import unittest\n" + correct.replace(
                b"    return 0", b"    if amount == 100: raise unittest.SkipTest('skip')\n    return 0"),
            correct.replace(b"    return 0", b"    if amount == 100: return 1\n    return 0"),
        ]
        for original in alternatives:
            with self.subTest(original=original):
                root = self.golden_workspace()
                config = copy.deepcopy(self.workspace_grader())
                index = config["args"].index("--original") + 1
                config["args"][index] = "boundary.py:" + base64.b64encode(original).decode()
                result = self.run_command(root, config)
                self.assertNotEqual(0, result.returncode, result.stdout + result.stderr)
                self.assertFalse(list((root / ".eval").glob(".replay-*")))
        for selection in ("test_boundary.BoundaryTests.test_below_threshold",
                          "test_boundary.DoesNotExist"):
            with self.subTest(selection=selection):
                config = copy.deepcopy(self.workspace_grader())
                config["args"][config["args"].index("--selection") + 1] = selection
                self.assertNotEqual(0, self.run_command(
                    self.golden_workspace(), config).returncode)

    def test_installed_skill_is_the_only_optional_resource(self):
        root = self.golden_workspace()
        installed = root / "test-value-evidence"
        installed.mkdir()
        shutil.copyfile(
            ROOT / "plugins" / "dotnet-test" / "skills" / "test-value-evidence" / "SKILL.md",
            installed / "SKILL.md",
        )
        result = self.run_command(root, self.workspace_grader())
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        (installed / "extra.py").write_text("pass\n", encoding="utf-8")
        self.assertNotEqual(0, self.run_command(root, self.workspace_grader()).returncode)

    def test_production_vally_parser_and_command_grader(self):
        vally = ROOT / "eng" / "evaluation-tools" / "node_modules" / "@microsoft" / "vally" / "dist"
        script = """
            const {loadEvalSpec} = await import(process.argv[1]);
            const {gradeTrajectory} = await import(process.argv[2]);
            const {fromAtif} = await import(process.argv[5]);
            const {readFile} = await import('node:fs/promises');
            const {resolve, dirname} = await import('node:path');
            const spec = await loadEvalSpec(process.argv[3]);
            const results = [];
            let restraint = true;
            let provenance = true;
            for (const stimulus of spec.stimuli) {
                const document = stimulus.golden_trajectory.inline ??
                    JSON.parse(await readFile(resolve(dirname(process.argv[3]),
                        stimulus.golden_trajectory.path), 'utf8'));
                const trajectory = fromAtif(document, {
                    stimulusName: stimulus.name, stimulusPrompt: stimulus.prompt,
                    workDir: process.argv[4]
                });
                const graders = stimulus.graders.filter(g => g.type !== 'prompt');
                results.push(await gradeTrajectory(trajectory, graders, {stimulus}));
                if (stimulus === spec.stimuli[0]) {
                    const mutations = [
                        events => events.filter(e =>
                            e.type !== 'tool_call' && e.type !== 'tool_result'),
                        events => events.filter(e => e.data?.toolCallId !== 'original-tests'),
                        events => events.filter(e => e.data?.toolCallId !== 'fixed-tests'),
                        events => events.map(e => e.type === 'tool_result' &&
                            e.data.toolCallId === 'original-tests' ?
                            {...e, data:{...e.data, result:'Ran 3 tests in 0.001s\\nOK'}} : e),
                        events => events.map(e => e.type === 'tool_result' &&
                            e.data.toolCallId === 'original-tests' ?
                            {...e, data:{...e.data, result:String(e.data.result)
                                .replaceAll('test_exact_threshold', 'test_unrelated_setup')}} : e),
                        events => events.map(e => e.type === 'tool_result' &&
                            e.data.toolCallId === 'original-tests' ?
                            {...e, data:{...e.data, result:String(e.data.result)
                                .replace('Ran 3 tests', 'Ran 0 tests')}} : e),
                        events => events.map(e => e.type === 'tool_result' &&
                            e.data.toolCallId === 'fixed-tests' ?
                            {...e, data:{...e.data, result:String(e.data.result)
                                .replace('\\nOK', '\\nOK (skipped=1)')}} : e),
                        events => events.map(e => e.type === 'tool_call' &&
                            e.data.toolCallId === 'original-tests' ?
                            {...e, data:{...e.data,
                                arguments:{command:'echo fabricated output'}}} : e),
                    ];
                    for (const mutate of mutations) {
                        const mutated = structuredClone(trajectory);
                        mutated.events = mutate(mutated.events);
                        const graded = await gradeTrajectory(mutated,
                            graders.filter(g => g.type === 'tool-calls'), {stimulus});
                        provenance &&= !graded.passed;
                    }
                }
                if (stimulus.constraints) {
                    for (const toolName of ['bash', 'edit', 'create']) {
                        const mutated = structuredClone(trajectory);
                        const toolCallId = 'forbidden-call';
                        mutated.events.push(
                            {type:'tool_call', data:{toolCallId, toolName,
                                arguments:{command:'python -m unittest'}}},
                            {type:'tool_result', data:{toolCallId, toolName,
                                result:'synthetic forbidden execution'}}
                        );
                        const graded = await gradeTrajectory(mutated, graders, {stimulus});
                        restraint &&= !graded.passed;
                    }
                }
            }
            console.log(JSON.stringify({results, restraint, provenance}));
            process.exit(results.every(r => r.passed) && restraint && provenance ? 0 : 1);
        """
        root = self.golden_workspace()
        command = [
            "node", "--input-type=module", "-e", script,
            (vally / "eval" / "loader.js").as_uri(),
            (vally / "pipeline" / "grading.js").as_uri(),
            str(SUITE / "eval.yaml"), str(root),
            (vally / "trajectory" / "atif-adapter.js").as_uri(),
        ]
        passed = subprocess.run(command, capture_output=True, text=True, timeout=30)
        self.assertEqual(0, passed.returncode, passed.stdout + passed.stderr)
        graded = json.loads(passed.stdout)
        results = graded["results"]
        self.assertEqual(12, len(results))
        self.assertTrue(graded["restraint"])
        self.assertTrue(graded["provenance"])
        replay = next(detail for detail in results[0]["details"]
                      if detail["name"].startswith("run-command"))
        self.assertEqual([1, 0], [run["exit"] for run in json.loads(
            replay["metadata"]["stdout"])["runs"]])
        product = root / "boundary.py"
        no_lf_bytes = product.read_bytes()[:-1]
        product.write_bytes(no_lf_bytes)
        no_final_lf = subprocess.run(command, capture_output=True, text=True, timeout=30)
        self.assertEqual(0, no_final_lf.returncode, no_final_lf.stdout + no_final_lf.stderr)
        self.assertTrue(all(result["passed"] for result in json.loads(
            no_final_lf.stdout)["results"]))
        product.write_bytes(no_lf_bytes + b"\n# unrelated comment\n")
        unrelated = subprocess.run(command, capture_output=True, text=True, timeout=30)
        self.assertEqual(1, unrelated.returncode, unrelated.stdout + unrelated.stderr)
        product.write_bytes(no_lf_bytes)
        (root / "unexpected.txt").write_text("unexpected\n", encoding="utf-8")
        rejected = subprocess.run(command, capture_output=True, text=True, timeout=30)
        self.assertEqual(1, rejected.returncode, rejected.stdout + rejected.stderr)

    def test_all_advisory_cases_reject_execution_and_edits(self):
        for stimulus in SPEC["stimuli"][1:]:
            with self.subTest(stimulus=stimulus["name"]):
                self.assertEqual({"bash", "edit", "create"},
                                 set(stimulus["constraints"]["reject_tools"]))
                restraint = next(grader for grader in stimulus["graders"]
                                 if grader["type"] == "tool-calls")
                self.assertEqual({"bash", "edit", "create"},
                                 set(restraint["config"]["disallowed"]))

    def test_portable_single_file_and_native_discovery(self):
        skill = ROOT / "plugins" / "dotnet-test" / "skills" / "test-value-evidence" / "SKILL.md"
        text = skill.read_text(encoding="utf-8")
        self.assertNotRegex(text, r"(?i)testfx|stryker|copilot-instructions\.md")
        self.assertNotRegex(text, r"\]\([^)]*SKILL\.md")
        self.assertIn("self-contained", text)
        self.assertLess(len(text.splitlines()), 500)
        self.assertNotIn("TEST_FILE_SHA256", (SUITE / "eval.yaml").read_text(encoding="utf-8"))
        test_hash = hashlib.sha256((SUITE / "fixtures" / "test_boundary.py").read_bytes()).hexdigest()
        self.assertIn(test_hash, (SUITE / "eval.yaml").read_text(encoding="utf-8"))
        for manifest in ("plugin.json", ".claude-plugin/plugin.json", ".codex-plugin/plugin.json"):
            data = yaml.safe_load((ROOT / "plugins" / "dotnet-test" / manifest).read_text(encoding="utf-8"))
            self.assertEqual(["./skills/"], data["skills"])
        active = sum(item.get("expect_activation", True) for item in SPEC["stimuli"])
        self.assertEqual(9, active)
        self.assertEqual(3, len(SPEC["stimuli"]) - active)


if __name__ == "__main__":
    unittest.main()
