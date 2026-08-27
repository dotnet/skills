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
TEST_WORKFLOW = REPO_ROOT / ".github" / "workflows" / "evaluation-workflow-tests.yml"
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


def token_unavailable_pattern() -> str:
    workflow = yaml.safe_load(WORKFLOW.read_text(encoding="utf-8"))
    return workflow["jobs"]["vally-evaluate"]["env"][
        "COPILOT_TOKEN_UNAVAILABLE_PATTERN"
    ]


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
model=""
has_effort=false
while [ "$#" -gt 0 ]; do
  case "$1" in
    --model) model="$2"; shift 2 ;;
    --effort=*) has_effort=true; shift ;;
    *) shift ;;
  esac
done
echo "$model" >> "$MODELS"
if [ "$model" = "no-effort-model" ] && [ "$has_effort" = true ]; then
  echo 'Error: Model "no-effort-model" does not support reasoning effort configuration (requested: "low").' >&2
  exit 1
fi
case "$COPILOT_GITHUB_TOKEN" in
  rate-limited) echo "403 API rate limit exceeded" >&2; exit 1 ;;
  weekly-rate-limited) echo '{"type":"session.error","data":{"errorType":"rate_limit","errorCode":"user_weekly_rate_limited","message":"You have reached your weekly rate limit"}}' >&2; exit 1 ;;
  status-429) echo "Request failed with status code 429" >&2; exit 1 ;;
  too-many-requests) echo "Too Many Requests" >&2; exit 1 ;;
  weekly-message) echo "You have reached your weekly rate limit" >&2; exit 1 ;;
  timed-out) exit 124 ;;
  unauthorized) echo "401 Unauthorized" >&2; exit 7 ;;
  unauthorized-after-effort) echo "401 Unauthorized after effort retry" >&2; exit 7 ;;
  disabled) echo "This organization has been disabled" >&2; exit 8 ;;
  service-error) echo "Unexpected internal service failure" >&2; exit 9 ;;
  model-error) echo "Model gpt-401 not found" >&2; exit 10 ;;
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
                    "COPILOT_TOKEN_UNAVAILABLE_PATTERN": token_unavailable_pattern(),
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

    def test_model_without_effort_support_is_retried_without_effort(self) -> None:
        result = self.run_selector(
            {0: "healthy"},
            model="no-effort-model",
            judge_model="judge-model",
        )

        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.attempts, ["healthy", "healthy", "healthy"])
        self.assertEqual(
            result.models,
            ["no-effort-model", "no-effort-model", "judge-model"],
        )
        self.assertEqual(result.selected_token, "healthy")
        self.assertIn("retrying its availability probe without --effort", result.stdout)

    def test_model_without_effort_support_fails_over_after_one_retry(self) -> None:
        result = self.run_selector(
            {0: "unauthorized-after-effort", 1: "healthy"},
            model="no-effort-model",
            judge_model="judge-model",
        )

        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(
            result.attempts,
            [
                "unauthorized-after-effort",
                "unauthorized-after-effort",
                "healthy",
                "healthy",
                "healthy",
            ],
        )
        self.assertEqual(
            result.models,
            [
                "no-effort-model",
                "no-effort-model",
                "no-effort-model",
                "no-effort-model",
                "judge-model",
            ],
        )
        self.assertEqual(result.selected_token, "healthy")
        self.assertIn("has unusable credentials", result.stdout)
        self.assertIn("401 Unauthorized after effort retry", result.stdout)

    def test_unavailable_candidate_fails_over_to_healthy_candidate(self) -> None:
        for unavailable_token in ("unauthorized", "disabled"):
            with self.subTest(unavailable_token=unavailable_token):
                result = self.run_selector(
                    {0: unavailable_token, 1: "healthy"}
                )

                self.assertEqual(result.returncode, 0, result.stderr)
                self.assertEqual(
                    result.attempts, [unavailable_token, "healthy"]
                )
                self.assertEqual(result.selected_token, "healthy")
                self.assertIn(
                    "quarantining it and trying another entry", result.stdout
                )

    def test_unrelated_failure_does_not_try_another_candidate(self) -> None:
        for failing_token in ("service-error", "model-error"):
            with self.subTest(failing_token=failing_token):
                result = self.run_selector(
                    {0: failing_token, 1: "healthy"}
                )

                self.assertNotEqual(result.returncode, 0)
                self.assertEqual(result.attempts, [failing_token])
                self.assertIsNone(result.selected_token)
                self.assertIn(
                    "unexpected non-rate-limit error", result.stdout
                )
                self.assertIn(
                    "refusing to hide a service or configuration failure",
                    result.stdout,
                )

    def test_all_unavailable_candidates_fail_clearly(self) -> None:
        result = self.run_selector({0: "unauthorized", 1: "disabled"})

        self.assertEqual(result.returncode, 1)
        self.assertEqual(result.attempts, ["unauthorized", "disabled"])
        self.assertIsNone(result.selected_token)
        self.assertIn(
            "No healthy Copilot PAT pool entry was found", result.stdout
        )
        self.assertIn(
            "at least one configured entry was unavailable", result.stdout
        )

    def test_all_rate_limited_candidates_fail_clearly(self) -> None:
        result = self.run_selector({0: "rate-limited", 1: "weekly-rate-limited"})

        self.assertEqual(result.returncode, 1)
        self.assertEqual(result.attempts, ["rate-limited", "weekly-rate-limited"])
        self.assertIsNone(result.selected_token)
        self.assertIn("Every configured Copilot PAT pool entry is rate-limited", result.stdout)

    def test_token_unavailable_pattern_matches_credential_failures(self) -> None:
        pattern = token_unavailable_pattern()

        for message in (
            "Failed to fetch PAT user login (401): Bad credentials.",
            "Authentication token found but could not be validated.",
            "The authentication token has expired.",
            "This organization has been disabled.",
            "Copilot access was disabled by your organization.",
        ):
            with self.subTest(message=message):
                env = os.environ.copy()
                env.update({"PATTERN": pattern, "MESSAGE": message})
                result = subprocess.run(
                    [BASH, "-c", 'printf "%s\\n" "$MESSAGE" | grep -Eiq "$PATTERN"'],
                    env=env,
                    check=False,
                )
                self.assertEqual(result.returncode, 0, message)

        for message in (
            "Unexpected internal service failure",
            "Internal server error: request id req-2401 failed",
            "Upstream returned HTTP 500 after 2.401 seconds",
            "Model gpt-401 not found",
            "Processed 12401 tokens before crashing",
            "Service unavailable: token bucket refill expired",
            "Configuration error: organization policy disabled telemetry",
        ):
            with self.subTest(message=message):
                env = os.environ.copy()
                env.update({"PATTERN": pattern, "MESSAGE": message})
                result = subprocess.run(
                    [BASH, "-c", 'printf "%s\\n" "$MESSAGE" | grep -Eiq "$PATTERN"'],
                    env=env,
                    check=False,
                )
                self.assertEqual(result.returncode, 1, message)

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
        self.assertIn(
            '"$RUNNER_TEMP/trusted-validator-src/eng/evaluation-tools/package-lock.json"',
            install_script,
        )
        self.assertIn("npm ci", install_script)
        self.assertNotIn("npm install", install_script)
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

    def test_evaluation_tool_manifest_has_secretless_smoke_test(self) -> None:
        workflow = yaml.safe_load(TEST_WORKFLOW.read_text(encoding="utf-8"))
        triggers = workflow.get("on", workflow.get(True))
        tool_path = "eng/evaluation-tools/**"
        for event in ("pull_request", "push"):
            self.assertEqual(triggers[event]["paths"].count(tool_path), 1)

        job = workflow["jobs"]["evaluation-tools"]
        self.assertEqual(job["runs-on"], "ubuntu-latest")
        steps = {step.get("name"): step for step in job["steps"]}
        install_script = steps["Install evaluation tools"]["run"]
        self.assertIn("--prefix eng/evaluation-tools", install_script)
        self.assertIn("npm ci", install_script)
        self.assertNotIn("npm install", install_script)
        self.assertIn("--registry https://registry.npmjs.org/", install_script)

        smoke_script = steps["Smoke test evaluation tools"]["run"]
        self.assertIn("node_modules/.bin/vally --version", smoke_script)
        self.assertIn("node_modules/.bin/copilot --version", smoke_script)
        self.assertIn(
            "import.meta.resolve('@github/copilot-linux-x64/sdk')",
            smoke_script,
        )

    def test_adapter_fault_injection_runs_in_pr_ci(self) -> None:
        workflow = yaml.safe_load(TEST_WORKFLOW.read_text(encoding="utf-8"))
        triggers = workflow.get("on", workflow.get(True))
        adapter_path = "eng/vally-adapter/**"
        for event in ("pull_request", "push"):
            self.assertEqual(triggers[event]["paths"].count(adapter_path), 1)

        job = workflow["jobs"]["vally-adapter"]
        self.assertEqual(job["runs-on"], "ubuntu-latest")
        steps = {step.get("name"): step for step in job["steps"]}
        self.assertIn(
            "node --test eng/vally-adapter/*.test.mjs",
            steps["Run adapter fault-injection and report tests"]["run"],
        )

    def test_pr_report_binds_identity_and_reruns_to_exact_commit(self) -> None:
        workflow = yaml.safe_load(CALLER_WORKFLOW.read_text(encoding="utf-8"))
        comment_job = workflow["jobs"]["comment-on-pr"]
        steps = {
            step.get("name"): step
            for step in comment_job["steps"]
        }
        script = steps["Consolidate and post results"]["run"]

        self.assertEqual(
            script.count(
                '--commit "${{ needs.gate.outputs.head_sha }}"'
            ),
            2,
        )
        self.assertIn(
            "To investigate non-passing or warning results",
            script,
        )
        self.assertIn(
            "comment `/evaluate %s` to retry this exact commit",
            script,
        )
        self.assertNotIn("re-post `/evaluate`", script)

    def test_partial_matrix_results_never_become_complete_verdicts(self) -> None:
        caller = yaml.safe_load(CALLER_WORKFLOW.read_text(encoding="utf-8"))
        comment_job = caller["jobs"]["comment-on-pr"]
        self.assertNotIn(
            "needs.evaluate.result != 'cancelled'",
            comment_job["if"],
        )

        comment_steps = {
            step.get("name"): step for step in comment_job["steps"]
        }
        consolidate_step = comment_steps["Consolidate and post results"]
        self.assertEqual(consolidate_step["if"], "always()")
        self.assertEqual(
            consolidate_step["env"]["EXPECTED_ENTRIES"],
            "${{ needs.discover.outputs.entries }}",
        )
        script = consolidate_step["run"]
        incomplete_guard = (
            'if [[ "$MATRIX_MANIFEST_VALID" != "true" '
            '|| "$EVALUATE_RESULT" != "success" '
            '|| "$OBSERVED_LEG_COUNT" -ne "$EXPECTED_LEG_COUNT" ]]'
        )
        guard_index = script.index(incomplete_guard)
        consolidation_index = script.index(
            "node eng/vally-adapter/consolidate.mjs"
        )
        self.assertLess(guard_index, consolidation_index)
        self.assertIn(
            "were preserved for diagnosis but were not consolidated",
            script[guard_index:consolidation_index],
        )
        self.assertIn(
            "exit 0",
            script[guard_index:consolidation_index],
        )
        self.assertIn(
            "find all-results/ -name adapter-summary.json",
            script[:guard_index],
        )
        self.assertIn(
            "if ! EXPECTED_LEG_COUNT=$(printf",
            script[:guard_index],
        )
        self.assertIn(
            "the discovered entry list was missing, malformed, or not a JSON array",
            script[guard_index:consolidation_index],
        )
        self.assertIn(
            "expected %s matrix leg artifact(s), but found %s",
            script[guard_index:consolidation_index],
        )

        discover_script = next(
            step["run"]
            for step in caller["jobs"]["discover"]["steps"]
            if "function Get-PluginShardEntries" in step.get("run", "")
        )
        self.assertIn(
            'if (-not (Test-Path $evalPath)) { continue }',
            discover_script,
        )
        self.assertIn(
            'if ($shardGroups.Count -eq 0) { return @() }',
            discover_script,
        )

        runner = yaml.safe_load(WORKFLOW.read_text(encoding="utf-8"))
        runner_steps = {
            step.get("name"): step
            for step in runner["jobs"]["vally-evaluate"]["steps"]
        }
        self.assertEqual(
            runner_steps["Upload results"]["with"]["if-no-files-found"],
            "error",
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

        download = by_name["Download trusted skill-validator archive"]
        self.assertTrue(download["uses"].startswith("actions/download-artifact@"))
        self.assertEqual(
            download["with"]["name"],
            "trusted-skill-validator-${{ github.run_id }}",
        )
        self.assertEqual(
            download["with"]["path"],
            "${{ runner.temp }}/trusted-validator-archive",
        )
        self.assertFalse(
            any(
                step.get("uses", "").startswith(
                    ("actions/cache", "actions/setup-dotnet")
                )
                for step in steps
            )
        )
        self.assertFalse(
            any("dotnet publish" in step.get("run", "") for step in steps)
        )
        producer_steps = workflow["jobs"]["prepare-validator"]["steps"]
        producer_by_name = {step.get("name"): step for step in producer_steps}
        producer_restore = producer_by_name["Restore skill-validator archive"]
        producer_save = producer_by_name["Save skill-validator archive"]
        producer_upload = producer_by_name["Upload trusted skill-validator archive"]
        self.assertTrue(producer_save["uses"].startswith("actions/cache/save@"))
        self.assertIn(
            "github.event_name != 'issue_comment'",
            producer_save["if"],
        )
        self.assertEqual(
            producer_restore["with"]["key"],
            "${{ steps.cache-key.outputs.key }}",
        )
        self.assertTrue(
            producer_upload["uses"].startswith("actions/upload-artifact@")
        )
        self.assertEqual(
            producer_upload["with"]["name"],
            download["with"]["name"],
        )
        self.assertEqual(
            producer_upload["with"]["path"],
            "skill-validator-dist.tar.gz",
        )
        self.assertEqual(
            producer_upload["with"]["if-no-files-found"],
            "error",
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

        extract_script = by_name["Extract skill-validator"]["run"]
        self.assertIn(
            '"$RUNNER_TEMP/trusted-validator-archive/skill-validator-dist.tar.gz"',
            extract_script,
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
            'The result set is incomplete or contains an unexpected eval.',
            run_script,
        )
        self.assertEqual(
            run_script.count(
                '--expected-evals "$RUNNER_TEMP/evaluation-expected-evals.txt"'
            ),
            2,
        )
        self.assertIn(
            'if [ "$PRODUCED" -ne "$EXPECTED_EVAL_COUNT" ]',
            run_script,
        )
        self.assertIn("s.expectedManifestProvided === true", run_script)
        self.assertIn("s.unexpectedEvalCount === 0", run_script)
        self.assertIn("s.measurementInvalidEvalCount === 0", run_script)
        self.assertNotIn("s.invalidEvalCount === 0", run_script)
        self.assertIn(
            "Vally comparison watchdog expired after 45 minutes",
            run_script,
        )
        self.assertIn(
            "retry-executor-timeouts.mjs",
            run_script,
        )
        self.assertIn(
            '--max-groups 3',
            run_script,
        )
        self.assertIn(
            'EXECUTOR_RETRY_STATUS=$?',
            run_script,
        )
        self.assertIn(
            'if [ "$EXECUTOR_RETRY_STATUS" -ne 0 ]',
            run_script,
        )
        self.assertLess(
            run_script.index("retry-executor-timeouts.mjs"),
            run_script.index(
                'node "$RUNNER_TEMP/trusted-validator-src/'
                'eng/vally-adapter/adapt.mjs"'
            ),
        )
        summary_script = by_name["Write summary"]["run"]
        self.assertIn('ICON="➖"', summary_script)
        self.assertNotIn('ICON="❌"', summary_script)
        self.assertNotIn(
            "watchdog expired after 45 minutes; uploading partial results",
            run_script,
        )
        find_script = by_name["Find eval specs"]["run"]
        self.assertIn(
            'printf \'%s\\n\' "$EVALS" > "$RUNNER_TEMP/evaluation-expected-evals.txt"',
            find_script,
        )
        self.assertIn('echo "count=$EVAL_COUNT" >> "$GITHUB_OUTPUT"', find_script)
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

    def test_result_consumers_use_explicit_verdict_states(self) -> None:
        workflow = yaml.safe_load(WORKFLOW.read_text(encoding="utf-8"))
        steps = workflow["jobs"]["vally-evaluate"]["steps"]
        summary_script = next(
            step["run"] for step in steps if step.get("name") == "Write summary"
        )
        self.assertIn("INVALID_INCONCLUSIVE", summary_script)
        self.assertIn("VALID_REGRESSION", summary_script)
        self.assertIn("PREFERENCE_REGRESSED", summary_script)
        self.assertNotIn("v.regressed ? 'VALID_REGRESSION'", summary_script)
        self.assertIn("v.state == null", summary_script)

        caller_text = CALLER_WORKFLOW.read_text(encoding="utf-8")
        self.assertIn("primaryState = $p.state", caller_text)
        self.assertIn(
            "$p.preferenceRegressed -eq $s.preferenceRegressed",
            caller_text,
        )


if __name__ == "__main__":
    unittest.main()
