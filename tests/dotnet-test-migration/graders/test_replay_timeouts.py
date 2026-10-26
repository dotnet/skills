"""Keep offline grader budgets aligned with production Vally."""

import json
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest
from unittest.mock import patch

import replay

ROOT = Path(__file__).resolve().parents[3]


class ReplayTimeoutTests(unittest.TestCase):
    def test_timeout_stops_descendants_before_late_workspace_writes(self):
        with tempfile.TemporaryDirectory(prefix="timeout-tree-") as directory:
            root = Path(directory)
            (root / "grandchild.py").write_text(
                "from pathlib import Path\nimport time\n"
                "Path('started').write_text('ready')\ntime.sleep(2)\n"
                "Path('late-write').write_text('escaped')\n", encoding="utf-8",
            )
            (root / "child.py").write_text(
                "import subprocess, sys, time\n"
                "subprocess.Popen([sys.executable, 'grandchild.py'], "
                "stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)\n"
                "time.sleep(10)\n", encoding="utf-8",
            )
            (root / "parent.py").write_text(
                "import subprocess, sys, time\n"
                "subprocess.Popen([sys.executable, 'child.py'], "
                "stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)\n"
                "time.sleep(10)\n", encoding="utf-8",
            )
            for kind in ("program", "run-command"):
                config = {"timeout": "1s"}
                if kind == "program":
                    config.update(program=sys.executable, args=["parent.py"])
                else:
                    config["command"] = f'"{sys.executable}" parent.py'
                with self.subTest(kind=kind), self.assertRaises(subprocess.TimeoutExpired):
                    replay.run_graders({"graders": [{"type": kind, "config": config}]}, root)
                self.assertTrue((root / "started").exists(), "Child did not start: invalid timeout control")
                time.sleep(2)
                self.assertFalse((root / "late-write").exists(), "Timed-out descendant wrote afterward")
                (root / "started").unlink()

    def test_exit_codes_and_interrupt_cleanup_remain_observable(self):
        with tempfile.TemporaryDirectory(prefix="replay-exit-") as directory:
            root = Path(directory)
            result = replay.run_command(
                [sys.executable, "-c", "import sys;print('failure detail');sys.exit(7)"],
                cwd=root, timeout=5, text=True, encoding="utf-8",
            )
            self.assertEqual(7, result.returncode)
            self.assertIn("failure detail", result.stdout)
            original = subprocess.Popen.communicate
            interrupted = False
            def interrupt(process, *args, **kwargs):
                nonlocal interrupted
                if not interrupted:
                    interrupted = True
                    try:
                        return original(process, *args, **dict(kwargs, timeout=0.5))
                    except subprocess.TimeoutExpired:
                        raise KeyboardInterrupt
                return original(process, *args, **kwargs)
            with patch.object(subprocess.Popen, "communicate", interrupt), self.assertRaises(KeyboardInterrupt):
                replay.run_command(
                    [sys.executable, "-c",
                     "from pathlib import Path;import time;"
                     "Path('started').write_text('ready');time.sleep(2);Path('late').write_text('escaped')"],
                    cwd=root, timeout=10, text=True, encoding="utf-8",
                )
            self.assertTrue((root / "started").exists())
            time.sleep(2)
            self.assertFalse((root / "late").exists())

    def configs(self):
        return (
            ("program", {"program": sys.executable,
                         "args": ["-c", 'print(\'{"passed": true}\')']}, 60),
            ("run-command", {"command": f'"{sys.executable}" -c "pass"'}, 30),
        )

    def test_duration_parsing_matches_production(self):
        valid = ["4m", "3m", "1m", "500ms", "1.5s", " 2. m ", "0.25h", "1d"]
        invalid = ["", "30", "1w", "1M", "1m30s", "-1s", ".5s", "0s", "0.0ms",
                   "NaNs", "9" * 400 + "d", "\u0661s", 30, False, None]
        script = """
import { readFileSync } from "node:fs";
import { parseDuration, parsePositiveDuration } from
  "./eng/evaluation-tools/node_modules/@microsoft/vally/dist/utils/duration.js";
const { valid, invalid } = JSON.parse(readFileSync(0, "utf8"));
console.log(JSON.stringify({
  seconds: valid.map(value => parseDuration(value) / 1000),
  rejected: invalid.map(value => !parsePositiveDuration(value).ok),
}));
"""
        result = subprocess.run(
            ["node", "--input-type=module", "-e", script], cwd=ROOT,
            input=json.dumps({"valid": valid, "invalid": invalid}),
            capture_output=True, text=True, check=True, timeout=30,
        )
        production = json.loads(result.stdout)
        self.assertEqual(production["seconds"],
                         [replay.grader_timeout({"timeout": value}, 0) for value in valid])
        self.assertTrue(all(production["rejected"]))
        for value in invalid[:-1]:
            with self.subTest(value=value), self.assertRaises(ValueError):
                replay.grader_timeout({"timeout": value}, 60)

    def test_both_call_sites_honor_declared_and_default_budgets(self):
        with tempfile.TemporaryDirectory(prefix="replay-timeout-") as directory:
            for kind, base, default in self.configs():
                for value, seconds in ((None, default), ("1m", 60), ("3m", 180),
                                       ("4m", 240), ("250ms", 0.25)):
                    with self.subTest(kind=kind, value=value), patch.object(
                        replay, "run_command"
                    ) as run:
                        run.return_value = subprocess.CompletedProcess([], 0, '{"passed": true}', "")
                        replay.run_graders({"graders": [{
                            "type": kind, "config": dict(base, timeout=value),
                        }]}, Path(directory))
                        self.assertEqual(seconds, run.call_args.kwargs["timeout"])
                with patch.object(replay, "run_command") as run:
                    run.return_value = subprocess.CompletedProcess([], 0, '{"passed": true}', "")
                    replay.run_graders({"graders": [{"type": kind, "config": base}]}, Path(directory))
                    self.assertEqual(default, run.call_args.kwargs["timeout"])

    def test_invalid_budgets_never_launch_commands(self):
        with tempfile.TemporaryDirectory(prefix="replay-invalid-timeout-") as directory:
            for kind, base, _ in self.configs():
                for value in ("0s", "20", -1, "9" * 400 + "d"):
                    with self.subTest(kind=kind, value=value), patch.object(
                        replay, "run_command"
                    ) as run:
                        with self.assertRaises(ValueError):
                            replay.run_graders({"graders": [{
                                "type": kind, "config": dict(base, timeout=value),
                            }]}, Path(directory))
                        run.assert_not_called()

    def test_slow_graders_time_out_instead_of_certifying_a_golden(self):
        with tempfile.TemporaryDirectory(prefix="replay-expiring-") as directory:
            for kind, base, _ in self.configs():
                config = dict(base, timeout="50ms")
                if kind == "program":
                    config["args"] = ["-c", 'import time; time.sleep(0.5); print(\'{"passed": true}\')']
                else:
                    config["command"] = f'"{sys.executable}" -c "import time; time.sleep(0.5)"'
                with self.subTest(kind=kind), self.assertRaises(subprocess.TimeoutExpired):
                    replay.run_graders({"graders": [{"type": kind, "config": config}]}, Path(directory))

    def test_fast_graders_pass_within_the_budget(self):
        with tempfile.TemporaryDirectory(prefix="replay-within-budget-") as directory:
            for kind, base, _ in self.configs():
                with self.subTest(kind=kind):
                    replay.run_graders({"graders": [{
                        "type": kind, "config": dict(base, timeout="5s"),
                    }]}, Path(directory))


if __name__ == "__main__":
    unittest.main()
