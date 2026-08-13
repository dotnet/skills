#!/usr/bin/env python3

import os
import stat
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

try:
    import yaml
except ImportError:  # pragma: no cover
    print("PyYAML is required: pip install pyyaml", file=sys.stderr)
    raise SystemExit(2)


REPO_ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = REPO_ROOT / ".github" / "workflows" / "evaluation-run.yml"
CALLER_WORKFLOW = REPO_ROOT / ".github" / "workflows" / "evaluation.yml"
STEP_NAME = "Select available Copilot token from pool"
GIT_BASH = Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "Git" / "bin" / "bash.exe"
BASH = str(GIT_BASH) if os.name == "nt" and GIT_BASH.exists() else "bash"


def selection_script() -> str:
    workflow = yaml.safe_load(WORKFLOW.read_text(encoding="utf-8"))
    try:
        steps = workflow["jobs"]["vally-evaluate"]["steps"]
    except (KeyError, TypeError) as error:
        raise AssertionError(
            f"{WORKFLOW} does not define jobs.vally-evaluate.steps"
        ) from error
    for step in steps:
        if step.get("name") == STEP_NAME:
            return step["run"]
    raise AssertionError(f"{WORKFLOW} does not contain the '{STEP_NAME}' step")


def rate_limit_pattern() -> str:
    workflow = yaml.safe_load(WORKFLOW.read_text(encoding="utf-8"))
    return workflow["jobs"]["vally-evaluate"]["env"]["COPILOT_RATE_LIMIT_PATTERN"]


class TokenFailoverTests(unittest.TestCase):
    def run_selector(
        self,
        tokens: dict[int, str],
        model: str = "claude-opus-4.6",
        judge_model: str = "claude-opus-4.6",
    ) -> subprocess.CompletedProcess[str]:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            fake_bin = root / "bin"
            fake_bin.mkdir()
            attempts = root / "attempts"
            models = root / "models"
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
while [ "$#" -gt 0 ]; do
  if [ "$1" = "--model" ]; then
    echo "$2" >> "$MODELS"
    break
  fi
  shift
done
case "$COPILOT_GITHUB_TOKEN" in
  rate-limited) echo "403 API rate limit exceeded" >&2; exit 1 ;;
  weekly-rate-limited) echo '{"type":"session.error","data":{"errorType":"rate_limit","errorCode":"user_weekly_rate_limited","message":"You have reached your weekly rate limit"}}' >&2; exit 1 ;;
  status-429) echo "Request failed with status code 429" >&2; exit 1 ;;
  too-many-requests) echo "Too Many Requests" >&2; exit 1 ;;
  weekly-message) echo "You have reached your weekly rate limit" >&2; exit 1 ;;
  timed-out) exit 124 ;;
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
                    "MODELS": shell_path(models),
                    "GITHUB_OUTPUT": shell_path(github_output),
                    "RUNNER_TEMP": shell_path(root),
                    "PROBE_MODEL": model,
                    "PROBE_JUDGE_MODEL": judge_model,
                    "COPILOT_RATE_LIMIT_PATTERN": rate_limit_pattern(),
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
            result.models = (
                models.read_text(encoding="utf-8").splitlines()
                if models.exists()
                else []
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

    def test_probe_rate_limit_pattern_matches_common_wording(self) -> None:
        for limited_token in (
            "status-429",
            "too-many-requests",
            "weekly-message",
        ):
            with self.subTest(limited_token=limited_token):
                result = self.run_selector({0: limited_token, 1: "healthy"})

                self.assertEqual(result.returncode, 0, result.stderr)
                self.assertEqual(result.attempts, [limited_token, "healthy"])
                self.assertEqual(result.selected_token, "healthy")

    def test_timed_out_candidate_fails_over_to_healthy_candidate(self) -> None:
        result = self.run_selector({0: "timed-out", 1: "healthy"})

        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.attempts, ["timed-out", "healthy"])
        self.assertEqual(result.selected_token, "healthy")
        self.assertIn("entry 0 timed out", result.stdout)

    def test_distinct_agent_and_judge_models_are_both_probed(self) -> None:
        result = self.run_selector(
            {0: "healthy"},
            model="agent-model",
            judge_model="judge-model",
        )

        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.attempts, ["healthy", "healthy"])
        self.assertEqual(result.models, ["agent-model", "judge-model"])
        self.assertEqual(result.selected_token, "healthy")

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

    def test_actual_run_uses_shared_rate_limit_pattern(self) -> None:
        workflow = yaml.safe_load(WORKFLOW.read_text(encoding="utf-8"))
        steps = workflow["jobs"]["vally-evaluate"]["steps"]
        run_script = next(
            step["run"] for step in steps if step.get("name") == "Run vally evaluations"
        )
        self.assertIn(
            'grep -Eiq "$COPILOT_RATE_LIMIT_PATTERN" "$VALLY_LOG"',
            run_script,
        )
        pattern = rate_limit_pattern()

        for message in (
            "Request failed with status code 429",
            "403 API rate limit exceeded",
            "user_weekly_rate_limited",
            "Too Many Requests",
            "You have reached your weekly rate limit",
        ):
            env = os.environ.copy()
            env.update({"PATTERN": pattern, "MESSAGE": message})
            result = subprocess.run(
                [BASH, "-c", 'printf "%s\\n" "$MESSAGE" | grep -Eiq "$PATTERN"'],
                env=env,
                check=False,
            )
            self.assertEqual(result.returncode, 0, message)

        env = os.environ.copy()
        env.update({"PATTERN": pattern, "MESSAGE": "401 Unauthorized"})
        result = subprocess.run(
            [BASH, "-c", 'printf "%s\\n" "$MESSAGE" | grep -Eiq "$PATTERN"'],
            env=env,
            check=False,
        )
        self.assertEqual(result.returncode, 1)

    def test_eval_discovery_precedes_tool_install_and_token_selection(self) -> None:
        workflow = yaml.safe_load(WORKFLOW.read_text(encoding="utf-8"))
        steps = workflow["jobs"]["vally-evaluate"]["steps"]
        by_name = {step.get("name"): (index, step) for index, step in enumerate(steps)}

        find_index, _ = by_name["Find eval specs"]
        install_index, install = by_name["Install vally and Copilot CLI"]
        select_index, select = by_name[STEP_NAME]
        run_index, _ = by_name["Run vally evaluations"]

        self.assertLess(find_index, install_index)
        self.assertLess(install_index, select_index)
        self.assertLess(select_index, run_index)
        expected_condition = "steps.find-evals.outputs.has_evals == 'true'"
        self.assertEqual(install["if"], expected_condition)
        self.assertEqual(select["if"], expected_condition)
        install_script = install["run"]
        self.assertNotIn("npm install -g", install_script)
        self.assertIn(
            '--prefix "$RUNNER_TEMP/evaluation-tools"',
            install_script,
        )
        self.assertIn(
            '"$RUNNER_TEMP/trusted-validator-src/eng/evaluation-tools/package.json"',
            install_script,
        )
        self.assertNotIn("@microsoft/vally-cli@", install_script)
        self.assertNotIn("@github/copilot@", install_script)
        self.assertIn(
            '"$RUNNER_TEMP/evaluation-tools/node_modules/.bin" >> "$GITHUB_PATH"',
            install_script,
        )
        self.assertIn(
            "import.meta.resolve('@github/copilot-linux-x64/sdk')",
            install_script,
        )

    def test_fork_checkout_is_blocked_and_adapter_code_is_trusted(self) -> None:
        workflow = yaml.safe_load(WORKFLOW.read_text(encoding="utf-8"))
        steps = workflow["jobs"]["vally-evaluate"]["steps"]
        by_name = {step.get("name"): step for step in steps}

        checkout = by_name["Checkout skills content"]
        self.assertNotIn("allow-unsafe-pr-checkout", checkout["with"])

        caller = yaml.safe_load(CALLER_WORKFLOW.read_text(encoding="utf-8"))
        for job_name in ("evaluate", "publish-token-data", "publish-session-data"):
            condition = caller["jobs"][job_name]["if"]
            self.assertIn(
                "needs.gate.outputs.is_fork != 'true'",
                condition,
                f"{job_name} must not run for fork PR content",
            )
        self.assertIn(
            "inputs.pr_number == ''",
            caller["jobs"]["deploy-dashboard"]["if"],
        )

        restore = by_name["Restore skill-validator archive"]
        self.assertTrue(restore["uses"].startswith("actions/cache/restore@"))
        self.assertEqual(
            restore["with"]["key"],
            "${{ needs.prepare-validator.outputs.cache-key }}",
        )
        self.assertFalse(
            any(step.get("uses", "").startswith("actions/cache@") for step in steps)
        )
        producer_steps = workflow["jobs"]["prepare-validator"]["steps"]
        producer_by_name = {step.get("name"): step for step in producer_steps}
        producer_restore = producer_by_name["Restore skill-validator archive"]
        producer_save = producer_by_name["Save skill-validator archive"]
        self.assertTrue(producer_save["uses"].startswith("actions/cache/save@"))
        self.assertEqual(
            producer_restore["with"]["key"],
            "${{ steps.cache-key.outputs.key }}",
        )
        cache_key_script = producer_by_name["Resolve trusted cache key"]["run"]
        self.assertIn(
            "trusted-skill-validator-v1-",
            cache_key_script,
        )
        self.assertIn(
            "needs.prepare-validator.result == 'success'",
            workflow["jobs"]["vally-evaluate"]["if"],
        )

        stage_script = by_name["Stage trusted evaluation tooling"]["run"]
        self.assertIn(
            'cp -a "$GITHUB_WORKSPACE/_trusted-validator-src" '
            '"$RUNNER_TEMP/trusted-validator-src"',
            stage_script,
        )

        build_script = by_name["Build trusted skill-validator"]["run"]
        self.assertIn('cd "$RUNNER_TEMP/trusted-validator-src"', build_script)
        self.assertIn(
            '"eng/skill-validator/src/SkillValidator.csproj"',
            build_script,
        )

        run_script = by_name["Run vally evaluations"]["run"]
        self.assertIn(
            '[ ! -r "$RUNNER_TEMP/evaluation-copilot-token" ]',
            run_script,
        )
        self.assertIn(
            'echo "::error::No experiment output produced for $PLUGIN"',
            run_script,
        )
        self.assertIn(
            'echo "::error::vally produced no skill verdicts for $PLUGIN;',
            run_script,
        )
        self.assertIn(
            'grep -Eiq "$COPILOT_RATE_LIMIT_PATTERN" "$VALLY_LOG"',
            run_script,
        )
        self.assertIn('"$results_file" >/dev/null', run_script)
        self.assertIn('find "$EXPERIMENT_OUT" -name results.jsonl', run_script)
        self.assertIn(
            'echo "::error::Selected Copilot PAT became rate-limited during evaluation;',
            run_script,
        )
        self.assertIn('rm -rf "$EXPERIMENT_OUT"', run_script)
        trusted_adapter = '"$RUNNER_TEMP/trusted-validator-src/eng/vally-adapter/'
        self.assertIn(f"node {trusted_adapter}gen-experiment.mjs", run_script)
        self.assertIn(f"node {trusted_adapter}adapt.mjs", run_script)
        self.assertNotIn("node eng/vally-adapter/", run_script)


if __name__ == "__main__":
    unittest.main()
