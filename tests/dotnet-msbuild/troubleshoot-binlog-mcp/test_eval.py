import json
from pathlib import Path
import re
import shlex
import shutil
import subprocess
import tempfile
import unittest

import yaml


ROOT = Path(__file__).resolve().parent
SPEC = yaml.safe_load((ROOT / "eval.yaml").read_text(encoding="utf-8"))
LAUNCH = next(s for s in SPEC["stimuli"]
              if s["name"] == "Diagnose a closed connection by executing the registered launcher")
CAPTURE = next(s for s in SPEC["stimuli"] if s["name"] == "Leave binlog capture to build logging")


class LaunchGraderTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name).resolve()
        shutil.copytree(ROOT / "fixtures" / "launch-failure", self.root, dirs_exist_ok=True)
        self.workspace = self.root / "affected-workspace"
        self.server = json.loads((self.workspace / "host.json").read_text())["mcpServers"]["dotnet-msbuild"]

    def launch(self, *, cwd=None, extra_args=()):
        result = subprocess.run(
            [self.server["command"], str(self.workspace / self.server["args"][0]),
             *self.server["args"][1:], *extra_args],
            cwd=cwd or self.workspace, capture_output=True, text=True, timeout=30,
        )
        self.write_result(stdout=result.stdout, stderr=result.stderr, exitCode=result.returncode)

    def write_result(self, **result):
        (self.workspace / "launch-result.json").write_text(json.dumps(result), encoding="utf-8")

    def grade(self):
        config = next(g["config"] for g in LAUNCH["graders"] if g["type"] == "run-command")
        return subprocess.run(
            [config["command"], *config["args"]],
            cwd=self.root, capture_output=True, text=True, timeout=30,
        )

    def assert_rejected(self, message):
        result = self.grade()
        self.assertNotEqual(result.returncode, 0, result.stdout)
        self.assertIn(message, result.stderr)

    def test_golden_replay_passes(self):
        reference = json.loads((ROOT / LAUNCH["golden_trajectory"]["path"]).read_text())
        command = reference["steps"][0]["tool_calls"][0]["arguments"]["command"]
        subprocess.run(shlex.split(command), cwd=self.root, check=True, capture_output=True, timeout=30)
        result = self.grade()
        self.assertEqual(result.returncode, 0, result.stderr)

    def test_advice_without_execution_fails(self):
        self.assert_rejected("launch-invocations.jsonl")

    def test_search_without_launch_fails(self):
        subprocess.run(
            [self.server["command"], self.server["args"][0], "package", "search",
             "Microsoft.AITools.BinlogMcp", "--exact-match", "--prerelease", "--format", "json"],
            cwd=self.workspace, check=True, capture_output=True, timeout=30,
        )
        self.assert_rejected("Expected one actual launch")

    def test_wrong_workspace_fails(self):
        self.launch(cwd=self.root)
        self.assert_rejected("Launch must use the affected workspace")

    def test_equivalent_workspace_path_passes(self):
        self.launch()
        log = self.workspace / "launch-invocations.jsonl"
        record = json.loads(log.read_text())
        record["cwd"] = str(self.workspace / ".." / self.workspace.name)
        log.write_text(json.dumps(record) + "\n", encoding="utf-8")
        result = self.grade()
        self.assertEqual(result.returncode, 0, result.stderr)

    def test_changed_arguments_fail(self):
        self.launch(extra_args=("--source", "https://api.nuget.org/v3/index.json"))
        self.assert_rejected("Launch arguments must match")

    def test_duplicate_launch_fails(self):
        self.launch()
        self.launch()
        self.assert_rejected("Expected one actual launch")

    def test_missing_captured_evidence_fails(self):
        self.launch()
        report = json.loads((self.workspace / "launch-result.json").read_text())
        for field in ("stdout", "stderr", "exitCode"):
            with self.subTest(field=field):
                missing = {key: value for key, value in report.items() if key != field}
                self.write_result(**missing)
                self.assert_rejected("Missing or inaccurate captured " + field)

    def test_exit_code_must_be_numeric(self):
        self.launch()
        report = json.loads((self.workspace / "launch-result.json").read_text())
        report["exitCode"] = "1"
        self.write_result(**report)
        self.assert_rejected("Missing or inaccurate captured exitCode")

    def test_every_staged_input_is_preserved(self):
        self.launch()
        for source in sorted((ROOT / "fixtures" / "launch-failure").rglob("*")):
            if not source.is_file():
                continue
            target = self.root / source.relative_to(ROOT / "fixtures" / "launch-failure")
            original = target.read_bytes()
            with self.subTest(file=target.name):
                target.write_bytes(original + b"\n")
                self.assert_rejected("Changed or missing diagnostic input")
                target.write_bytes(original)

    def test_deleted_configuration_fails(self):
        self.launch()
        (self.workspace / "host.json").unlink()
        self.assert_rejected("host.json")


class CaptureGraderTests(unittest.TestCase):
    def test_unique_commands_and_broken_mutations(self):
        pattern = next(g["config"]["pattern"] for g in CAPTURE["graders"] if g["type"] == "output-matches")
        cases = [
            ("dotnet build '-bl:{}'", True),
            ('dotnet build "/bl:build-{}.binlog"', True),
            ('dotnet build -bl:"build-{}.binlog"', True),
            ("dotnet build /bl:'build-{}.binlog'", True),
            ('dotnet build "-bl:$([guid]::NewGuid()).binlog"', True),
            ('dotnet build -bl:"$([guid]::NewGuid()).binlog"', True),
            ('dotnet build "-bl:$([System.Guid]::NewGuid().ToString(\'N\')).binlog"', True),
            ('dotnet build "-bl:$(New-Guid).binlog"', True),
            ('dotnet build "-bl:$(Get-Date -Format yyyyMMdd-HHmmssfff).binlog"', True),
            ('dotnet build -bl:"$(Get-Date -Format yyyyMMdd-HHmmssfff).binlog"', True),
            ('$stamp = Get-Date -Format "yyyyMMdd_HHmmss"\n'
             'dotnet build "-bl:build_$stamp.binlog"', True),
            ('$log = "build-$([guid]::NewGuid().ToString(\'N\')).binlog"; '
             'dotnet build -bl:$log', True),
            ('$id = New-Guid; dotnet build "-bl:build-${id}.binlog"', True),
            ('$id = New-Guid; dotnet build -bl:"build-$id.binlog"', True),
            ('$stamp = Get-Date -Format "yyyyMMdd_HHmmss"; '
             'dotnet build -bl:"build-${stamp}.binlog"', True),
            ('$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"\n'
             'dotnet build MyProject.csproj -bl:"build_$timestamp.binlog"', True),
            ('dotnet build /bl:"build-$([DateTime]::Now.ToString(\'yyyyMMdd-HHmmssfff\'))-'
             '$([Guid]::NewGuid().ToString(\'N\')).binlog"', True),
            ("dotnet build '-bl:build.binlog'", False),
            ('dotnet build -bl:"build.binlog"', False),
            ('dotnet build "-bl:20261016-160000.binlog"', False),
            ("dotnet build -bl", False),
            ("dotnet build -bl:{}", False),
            ("dotnet build '-bl:{{}}'", False),
            ("dotnet build -bl:'{{}}'", False),
            ('dotnet build \'-bl:{}"', False),
            ('dotnet build -bl:\'build-{}.binlog"', False),
            ('$id = New-Guid; dotnet build "-bl:build.binlog"', False),
            ('$id = New-Guid; dotnet build "-bl:$other.binlog"', False),
            ('$id = New-Guid; dotnet build \'-bl:$id.binlog\'', False),
            ("$id = New-Guid; dotnet build -bl:'build-$id.binlog'", False),
            ("dotnet build -bl:'$([guid]::NewGuid()).binlog'", False),
            ('$id = New-Guid; dotnet build "-bl:$id_suffix.binlog"', False),
            ('$id = New-Guid; dotnet build "-bl:`$id.binlog"', False),
            ('$id = "fixed"; dotnet build "-bl:$id.binlog"', False),
            ('dotnet build "-bl:$(Get-Date -Format yyyy-MM-dd).binlog"', False),
        ]
        for output, expected in cases:
            with self.subTest(output=output):
                self.assertEqual(re.search(pattern, output) is not None, expected)
        script = (
            "const assert=require('node:assert/strict');"
            "const {pattern,cases}=JSON.parse(require('node:fs').readFileSync(0,'utf8'));"
            "const regex=new RegExp(pattern);"
            "for(const [output,expected] of cases)assert.equal(regex.test(output),expected,output);"
        )
        result = subprocess.run(
            ["node", "-e", script], input=json.dumps({"pattern": pattern, "cases": cases}),
            capture_output=True, text=True, timeout=30,
        )
        self.assertEqual(result.returncode, 0, result.stderr)

    def test_capture_golden_passes(self):
        pattern = next(g["config"]["pattern"] for g in CAPTURE["graders"] if g["type"] == "output-matches")
        output = CAPTURE["golden_trajectory"]["inline"]["steps"][0]["message"]
        self.assertIsNotNone(re.search(pattern, output))

    @unittest.skipUnless(shutil.which("pwsh"), "Native argument checks require PowerShell 7")
    def test_value_only_quotes_reach_native_executable_as_one_argument(self):
        script = r"""
            $ErrorActionPreference = 'Stop'
            node -e 'console.log(JSON.stringify(process.argv.slice(1)))' build '-bl:build-{}.binlog'
            node -e 'console.log(JSON.stringify(process.argv.slice(1)))' build -bl:"build-{}.binlog"
            node -e 'console.log(JSON.stringify(process.argv.slice(1)))' build -bl:"$([guid]::NewGuid()).binlog"
            $id = New-Guid
            node -e 'console.log(JSON.stringify(process.argv.slice(1)))' build -bl:"build-$id.binlog"
            $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
            node -e 'console.log(JSON.stringify(process.argv.slice(1)))' build -bl:"build-${stamp}.binlog"
        """
        result = subprocess.run(
            ["pwsh", "-NoProfile", "-NonInteractive", "-Command", script],
            capture_output=True, text=True, timeout=30,
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        arguments = [json.loads(line) for line in result.stdout.splitlines() if line.strip()]
        self.assertEqual(len(arguments), 5)
        for argv in arguments:
            self.assertEqual(len(argv), 2)
            self.assertEqual(argv[0], "build")
        self.assertEqual(arguments[0], ["build", "-bl:build-{}.binlog"])
        self.assertEqual(arguments[1], arguments[0])
        self.assertRegex(arguments[2][1], r"^-bl:[0-9a-f-]{36}\.binlog$")
        self.assertRegex(arguments[3][1], r"^-bl:build-[0-9a-f-]{36}\.binlog$")
        self.assertRegex(arguments[4][1], r"^-bl:build-\d{8}_\d{6}\.binlog$")


if __name__ == "__main__":
    unittest.main()
