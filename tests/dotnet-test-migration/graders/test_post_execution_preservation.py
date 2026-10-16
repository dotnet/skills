"""Preserved fixture bytes must survive the real build/test command."""

import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

import yaml

import migration_evidence
import replay


SPEC = Path(__file__).resolve().parent.parent / "migrate-xunit-to-mstest" / "eval.yaml"
CASE = next(
    case for case in yaml.safe_load(SPEC.read_text(encoding="utf-8"))["stimuli"]
    if case["name"] == "Preserve an ordinary shared collection without a disable-parallelization flag"
)


class PostExecutionPreservationTests(unittest.TestCase):
    def test_preserved_file_contract_checks_missing_and_changed_bytes(self):
        with tempfile.TemporaryDirectory(prefix="preserved-bytes-") as directory:
            root = Path(directory)
            source = root / "fixture.cs"
            original = b"class Fixture { }\n"
            digest = hashlib.sha256(original).hexdigest()
            contract = [f"{digest}:fixture.cs"]
            source.write_bytes(original.replace(b"\n", b"\r\n"))
            self.assertEqual({"fixture.cs": digest}, migration_evidence.verify_unchanged_files(root, contract))
            source.write_bytes(original + b"// changed\n")
            with self.assertRaisesRegex(AssertionError, "Preserved fixture changed"):
                migration_evidence.verify_unchanged_files(root, contract)
            source.unlink()
            with self.assertRaisesRegex(AssertionError, "Preserved fixture missing"):
                migration_evidence.verify_unchanged_files(root, contract)

    def test_real_post_build_rewrite_and_deletion_cannot_certify_preservation(self):
        for action in (
            '<WriteLinesToFile File="$(MSBuildProjectDirectory)/LifecycleProbe.cs" '
            'Lines="// changed after compilation" Overwrite="true" />',
            '<Delete Files="$(MSBuildProjectDirectory)/LifecycleProbe.cs" />',
        ):
            with self.subTest(action=action), tempfile.TemporaryDirectory(prefix="post-build-preserve-") as directory:
                root = Path(directory)
                replay.materialize(SPEC, CASE, root)
                probe = root / "LifecycleProbe.cs"
                original = probe.read_bytes()
                digest = hashlib.sha256(original.replace(b"\r\n", b"\n")).hexdigest()
                project = root / "TestProject.csproj"
                project.write_text(project.read_text(encoding="utf-8").replace(
                    "</Project>", f'<Target Name="MutatePreservedFile" AfterTargets="Build">'
                    f"{action}</Target></Project>",
                ), encoding="utf-8")
                command = [
                    sys.executable, ".eval/migration_evidence.py", "run",
                    "--project", "TestProject.csproj", "--runner", "vstest",
                    "--mstest-major", "4", "--lifecycle",
                    "--expect", "FirstCase=Passed", "--expect", "SecondCase=Passed",
                ]
                runtime = subprocess.run(
                    command, cwd=root, capture_output=True, text=True, timeout=180,
                )
                self.assertEqual(0, runtime.returncode, runtime.stdout + runtime.stderr)
                self.assertEqual(
                    {"FirstCase": "Passed", "SecondCase": "Passed"},
                    {name: record["outcome"] for name, record in json.loads(runtime.stdout)["cases"].items()},
                )
                self.assertTrue(not probe.exists() or probe.read_bytes() != original)
                probe.write_bytes(original)
                guarded = subprocess.run(
                    command + ["--unchanged-file", f"{digest}:LifecycleProbe.cs"],
                    cwd=root, capture_output=True, text=True, timeout=180,
                )
                self.assertNotEqual(0, guarded.returncode)
                self.assertIn("Preserved fixture", guarded.stderr)
                self.assertEqual("", guarded.stdout.strip())
                latest = max((root / ".eval" / "results").glob("migration-*"), key=lambda p: p.stat().st_mtime_ns)
                self.assertEqual(1, len(list(latest.glob("*.trx"))))
                self.assertFalse((latest / "evidence.json").exists())


if __name__ == "__main__":
    unittest.main()
