"""The repair no-op must preserve every supplied file, not merely pass tests."""

import json
from pathlib import Path
import shlex
import subprocess
import sys
import tempfile
import unittest

import yaml


SPEC = Path(__file__).with_name("eval.yaml")
DOCUMENT = yaml.safe_load(SPEC.read_text(encoding="utf-8"))
CASE = next(
    case for case in DOCUMENT["stimuli"]
    if case["name"] == "Leave already-correct tests unchanged during a repair request"
)
FILES = DOCUMENT["environment"]["files"] + CASE["environment"]["files"]


class RepairNoopTests(unittest.TestCase):
    def test_inspection_contract_does_not_treat_harness_execution_as_agent_execution(self):
        self.assertNotIn("constraints", CASE)
        config = next(
            grader["config"] for grader in CASE["graders"] if grader["type"] == "tool-calls"
        )
        golden = CASE["golden_trajectory"]["inline"]["steps"][-1]["message"]
        self.assertIn("fix anything that is actually broken", CASE["prompt"])
        self.assertIn("Do not execute tests or shell commands", CASE["prompt"])
        self.assertIn("did not execute tests", golden)
        self.assertNotIn("passed", golden)
        script = """
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { ToolCallGrader } from "./eng/evaluation-tools/node_modules/@microsoft/vally/dist/graders/static/tool-call-grader.js";
const config = JSON.parse(readFileSync(0, "utf8"));
const grader = new ToolCallGrader();
for (const name of ["view", "read_file", "glob", "functions.view",
                    "bash", "powershell", "execute", "run_shell_command", "task",
                    "functions.execute", "functions.powershell", "functions.task"]) {
  const allowed = ["view", "read_file", "glob", "functions.view"].includes(name);
  const data = { toolName: name, toolCallId: "one",
                 arguments: { command: "cd NoopRepair && python -m unittest -v" } };
  for (const success of [true, false]) {
    const result = await grader.grade({ config, trajectory: { events: [
      { type: "tool_call", data }, { type: "tool_result", data: { ...data, success, result: "Ran 2 tests OK" } }
    ] } });
    assert.equal(result.passed, allowed, name);
  }
}
assert.equal((await grader.grade({ config, trajectory: { events: [] } })).passed, true);
"""
        result = subprocess.run(
            ["node", "--input-type=module", "-e", script], cwd=SPEC.resolve().parents[3],
            input=json.dumps(config), capture_output=True, text=True, timeout=30,
        )
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_healthy_golden_and_every_file_mutation(self):
        with tempfile.TemporaryDirectory(prefix="repair-noop-") as directory:
            root = Path(directory)
            for item in FILES:
                target = root / item["dest"]
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes((SPEC.parent / item["src"]).read_bytes())
            command = shlex.split(CASE["graders"][0]["config"]["command"])
            command[0] = sys.executable

            def run_guard():
                return subprocess.run(command, cwd=root, capture_output=True, text=True, timeout=30)

            result = run_guard()
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            self.assertIn("Ran 2 tests", result.stderr)
            subprocess.run(
                [sys.executable, "-m", "unittest", "-v"], cwd=root / "NoopRepair",
                check=True, capture_output=True, text=True, timeout=30,
            )
            result = run_guard()
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            for item in FILES:
                path = root / item["dest"]
                original = path.read_bytes()
                for remove in (False, True):
                    with self.subTest(path=item["dest"], remove=remove):
                        if remove:
                            path.unlink()
                        else:
                            path.write_bytes(original + b"\n# unnecessary rewrite\n")
                        self.assertNotEqual(0, run_guard().returncode)
                        path.write_bytes(original)
            for name in (
                "NoopRepair/amount.py", "NoopRepair/__pycache__/hidden.py",
                "NoopRepair/nested/extra.txt", "amount.py", "unrelated/source.py",
                ".eval/extra.py",
            ):
                with self.subTest(addition=name):
                    addition = root / name
                    addition.parent.mkdir(parents=True, exist_ok=True)
                    addition.write_text("from amounts import total\n", encoding="utf-8")
                    self.assertNotEqual(0, run_guard().returncode)
                    addition.unlink()


if __name__ == "__main__":
    unittest.main()
