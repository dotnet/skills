#!/usr/bin/env python3

import os
import stat
import subprocess
import tempfile
import unittest
from pathlib import Path

import yaml


REPO_ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = REPO_ROOT / ".github" / "workflows" / "evaluation-run.yml"
STEP_NAME = "Select available Copilot token from pool"
GIT_BASH = Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "Git" / "bin" / "bash.exe"
BASH = str(GIT_BASH) if os.name == "nt" and GIT_BASH.exists() else "bash"


def selection_script() -> str:
    workflow = yaml.safe_load(WORKFLOW.read_text(encoding="utf-8"))
    steps = workflow["jobs"]["vally-evaluate"]["steps"]
    return next(step["run"] for step in steps if step.get("name") == STEP_NAME)


class TokenFailoverTests(unittest.TestCase):
    def run_selector(self, tokens: dict[int, str]) -> subprocess.CompletedProcess[str]:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            fake_bin = root / "bin"
            fake_bin.mkdir()
            attempts = root / "attempts"
            github_output = root / "github-output"
            token_file = root / "evaluation-copilot-token"
            fake_copilot = fake_bin / "copilot"
            fake_copilot.write_text(
                """#!/usr/bin/env bash
set -euo pipefail
if env | grep -Eq '^COPILOT_PAT_[0-9]='; then
  echo "PAT pool leaked to Copilot subprocess" >&2
  exit 11
fi
echo "$COPILOT_GITHUB_TOKEN" >> "$ATTEMPTS"
case "$COPILOT_GITHUB_TOKEN" in
  rate-limited) echo "403 API rate limit exceeded" >&2; exit 1 ;;
  weekly-rate-limited) echo '{"type":"session.error","data":{"errorType":"rate_limit","errorCode":"user_weekly_rate_limited","message":"You have reached your weekly rate limit"}}' >&2; exit 1 ;;
  unauthorized) echo "401 Unauthorized" >&2; exit 7 ;;
  healthy) exit 0 ;;
  *) echo "unexpected test token" >&2; exit 9 ;;
esac
""",
                encoding="utf-8",
            )
            fake_copilot.chmod(fake_copilot.stat().st_mode | stat.S_IXUSR)

            def shell_path(path: Path) -> str:
                if os.name != "nt":
                    return str(path)
                absolute = path.resolve()
                return f"/{absolute.drive[0].lower()}/{absolute.as_posix()[3:]}"

            env = os.environ.copy()
            env.update(
                {
                    "ATTEMPTS": shell_path(attempts),
                    "GITHUB_OUTPUT": shell_path(github_output),
                    "RUNNER_TEMP": shell_path(root),
                    "PROBE_MODEL": "claude-opus-4.6",
                    "TOKEN_RANDOM_SEED": "1",
                }
            )
            for index in range(10):
                env[f"COPILOT_PAT_{index}"] = tokens.get(index, "")

            result = subprocess.run(
                [
                    BASH,
                    "-c",
                    f'export PATH="{shell_path(fake_bin)}:$PATH"\n{selection_script()}',
                ],
                cwd=REPO_ROOT,
                env=env,
                text=True,
                capture_output=True,
                check=False,
            )
            result.attempts = (
                attempts.read_text(encoding="utf-8").splitlines()
                if attempts.exists()
                else []
            )
            result.selected_token = (
                token_file.read_text(encoding="utf-8") if token_file.exists() else None
            )
            result.github_output = (
                github_output.read_text(encoding="utf-8").splitlines()
                if github_output.exists()
                else []
            )
            return result

    def test_rate_limited_candidate_fails_over_to_healthy_candidate(self) -> None:
        result = self.run_selector({0: "rate-limited", 1: "healthy"})

        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.attempts, ["rate-limited", "healthy"])
        self.assertEqual(result.selected_token, "healthy")
        self.assertEqual(result.github_output, ["selected=1"])
        self.assertIn("entry 0 is rate-limited", result.stdout)

    def test_non_rate_limit_failure_does_not_try_another_token(self) -> None:
        result = self.run_selector({0: "unauthorized", 1: "healthy"})

        self.assertEqual(result.returncode, 7)
        self.assertEqual(result.attempts, ["unauthorized"])
        self.assertIsNone(result.selected_token)
        self.assertIn("non-rate-limit error", result.stdout)
        self.assertIn("401 Unauthorized", result.stdout)

    def test_all_rate_limited_candidates_fail_clearly(self) -> None:
        result = self.run_selector({0: "rate-limited", 1: "weekly-rate-limited"})

        self.assertEqual(result.returncode, 1)
        self.assertEqual(result.attempts, ["rate-limited", "weekly-rate-limited"])
        self.assertIsNone(result.selected_token)
        self.assertIn("Every configured Copilot PAT pool entry is rate-limited", result.stdout)


if __name__ == "__main__":
    unittest.main()
