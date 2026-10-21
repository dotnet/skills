from pathlib import Path
import hashlib
import re
import shutil
import subprocess
import sys
import tempfile
import unittest

import yaml


ROOT = Path(__file__).parents[3]
RUNNER = Path(__file__).with_name("authenticated_artifacts.py")
EVALS = (
    ROOT / "tests/dotnet-test/agent.test-engineer/eval.yaml",
    ROOT / "tests/dotnet-test/agent.test-quality-auditor/eval.yaml",
    ROOT / "tests/dotnet-test/agent.testability-migration/eval.yaml",
    ROOT / "tests/dotnet-test/code-testing/eval.yaml",
)
PATTERN = re.compile(
    r"hexdigest\(\)==['\"]([0-9a-f]{64})['\"]"
)
CLASSIC_BASELINE_PATTERN = re.compile(
    r"--expect ([0-9a-f]{64}):\.eval/classic-preservation\.json"
)
CLASSIC_EVALS = (
    ROOT / "tests/dotnet-test/agent.test-engineer/eval.yaml",
    ROOT / "tests/dotnet-test/code-testing/eval.yaml",
)
CLASSIC_CHECKER = (
    ROOT
    / "tests/dotnet-test/agent.test-engineer/graders/check_classic_preservation.py"
)


class EvalAuthenticationDigestTests(unittest.TestCase):
    def test_curated_auditor_grading_stages_readonly_composition_dependency(self):
        document = yaml.safe_load(EVALS[1].read_text(encoding="utf-8-sig"))
        stimulus = next(
            item for item in document["stimuli"]
            if item["name"] == "Route a curated test list to per-test decisions"
        )
        self.assertIn(
            "../../plugins/dotnet-test/skills/test-gap-analysis",
            stimulus["environment"]["skills"],
        )
        output_patterns = [
            grader["config"]["pattern"] for grader in stimulus["graders"]
            if grader["type"] == "output-matches"
        ]
        self.assertTrue(any("How to improve" in pattern for pattern in output_patterns))

    def test_specialist_stimuli_stage_their_authentication_helper(self):
        for path in EVALS[1:3]:
            document = yaml.safe_load(path.read_text(encoding="utf-8-sig"))
            self.assertNotIn("environment", document, path)
            for stimulus in document["stimuli"]:
                with self.subTest(path=path, stimulus=stimulus["name"]):
                    helpers = [
                        entry for entry in stimulus["environment"]["files"]
                        if entry["dest"] == ".eval/authenticated_artifacts.py"
                    ]
                    self.assertEqual(
                        [{"src": "../graders/authenticated_artifacts.py",
                          "dest": ".eval/authenticated_artifacts.py"}],
                        helpers,
                    )

    def test_migration_clock_contract_digests_match_canonical_files(self):
        eval_path = EVALS[2]
        text = eval_path.read_text(encoding="utf-8-sig")
        for contract in ("TimeContract", "ManualClockContract"):
            for name in ("Program.cs", f"{contract}.csproj"):
                source = eval_path.parent / "graders" / contract / name
                canonical = source.read_bytes().replace(b"\r\n", b"\n")
                expected = hashlib.sha256(canonical).hexdigest()
                matches = re.findall(
                    rf"--expect ([0-9a-f]{{64}}):\.eval/{contract}/{re.escape(name)}",
                    text,
                )
                self.assertTrue(matches, source)
                self.assertEqual({expected}, set(matches), source)

    def test_all_embedded_runner_digests_match_canonical_file(self):
        canonical = RUNNER.read_bytes().replace(b"\r\n", b"\n")
        expected = hashlib.sha256(canonical).hexdigest()
        for path in EVALS:
            text = path.read_text(encoding="utf-8-sig")
            self.assertNotIn("hashlib.sha256(p.read_bytes()).hexdigest()", text, path)
            digests = PATTERN.findall(text)
            self.assertTrue(digests, path)
            self.assertEqual({expected}, set(digests), path)

    def test_migration_scope_checker_digest_matches_canonical_file(self):
        eval_path = EVALS[2]
        checker = eval_path.parent / "graders/check_time_scope.py"
        expected = hashlib.sha256(checker.read_bytes().replace(b"\r\n", b"\n")).hexdigest()
        matches = re.findall(
            r"--expect ([0-9a-f]{64}):\.eval/check_time_scope\.py",
            eval_path.read_text(encoding="utf-8-sig"),
        )
        self.assertEqual([expected, expected], matches)

    def test_public_cart_baseline_digest_matches_current_fixture(self):
        eval_path = EVALS[0]
        checker = ROOT / "tests/dotnet-test/code-testing/graders/check_cart_tests.py"
        with tempfile.TemporaryDirectory() as directory:
            workspace = Path(directory)
            fixture = workspace / "cart"
            shutil.copytree(
                eval_path.parent / "fixtures/typescript-vitest-cart",
                fixture,
                ignore=shutil.ignore_patterns("node_modules", "bin", "obj"),
            )
            subprocess.run(
                [sys.executable, str(checker), "snapshot", "cart", ".eval/cart-baseline.json"],
                cwd=workspace, check=True,
            )
            baseline = (workspace / ".eval/cart-baseline.json").read_bytes().replace(b"\r\n", b"\n")
            expected = hashlib.sha256(baseline).hexdigest()
            matches = re.findall(
                r"--expect ([0-9a-f]{64}):\.eval/cart-baseline\.json",
                eval_path.read_text(encoding="utf-8-sig"),
            )
            self.assertEqual([expected], matches)

    def test_classic_baseline_digests_match_exact_eval_setup(self):
        for eval_path in CLASSIC_EVALS:
            with self.subTest(eval_path=eval_path), tempfile.TemporaryDirectory() as directory:
                workspace = Path(directory)
                fixture = workspace / "fixtures/classic-mstest"
                shutil.copytree(
                    eval_path.parent / "fixtures/classic-mstest",
                    fixture,
                    ignore=shutil.ignore_patterns("bin", "obj", "TestResults"),
                )
                for name in ("DiscountServiceBoundaryTests.cs", "TieredDiscountPolicyTests.cs"):
                    (fixture / "tests" / name).unlink(missing_ok=True)
                shutil.copy2(
                    fixture / "tests/Discounts.Tests.csproj.pristine",
                    fixture / "tests/Discounts.Tests.csproj",
                )
                (fixture / "tests/Discounts.Tests.csproj.pristine").unlink()
                subprocess.run(
                    [
                        sys.executable,
                        str(CLASSIC_CHECKER),
                        "snapshot",
                        "fixtures/classic-mstest",
                    ],
                    cwd=workspace,
                    check=True,
                )
                baseline = (workspace / ".eval/classic-preservation.json").read_bytes()
                expected = hashlib.sha256(baseline.replace(b"\r\n", b"\n")).hexdigest()
                embedded = CLASSIC_BASELINE_PATTERN.findall(
                    eval_path.read_text(encoding="utf-8-sig")
                )
                self.assertEqual([expected], embedded, eval_path)


if __name__ == "__main__":
    unittest.main()
