from pathlib import Path
import hashlib
import re
import shutil
import subprocess
import sys
import tempfile
import unittest


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
    def test_all_embedded_runner_digests_match_canonical_file(self):
        canonical = RUNNER.read_bytes().replace(b"\r\n", b"\n")
        expected = hashlib.sha256(canonical).hexdigest()
        for path in EVALS:
            text = path.read_text(encoding="utf-8-sig")
            self.assertNotIn("hashlib.sha256(p.read_bytes()).hexdigest()", text, path)
            digests = PATTERN.findall(text)
            self.assertTrue(digests, path)
            self.assertEqual({expected}, set(digests), path)

    def test_classic_baseline_digests_match_exact_eval_setup(self):
        for eval_path in CLASSIC_EVALS:
            with self.subTest(eval_path=eval_path), tempfile.TemporaryDirectory() as directory:
                workspace = Path(directory)
                fixture = workspace / "fixtures/classic-mstest"
                shutil.copytree(eval_path.parent / "fixtures/classic-mstest", fixture)
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
