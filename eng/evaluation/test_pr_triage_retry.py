#!/usr/bin/env python3

import os
import pathlib
import shutil
import subprocess
import tempfile
import textwrap
import unittest


REPO_ROOT = pathlib.Path(__file__).resolve().parents[2]
HELPER = REPO_ROOT / ".github" / "scripts" / "github-api-retry.sh"
WINDOWS_GIT_BASH = (
    pathlib.Path(os.environ.get("ProgramFiles", r"C:\Program Files"))
    / "Git"
    / "bin"
    / "bash.exe"
)
BASH = str(WINDOWS_GIT_BASH) if WINDOWS_GIT_BASH.exists() else shutil.which("bash")


def resolved_bash_path(resolved: pathlib.PurePath, platform: str = os.name) -> str:
    if platform != "nt":
        return resolved.as_posix()
    drive = resolved.drive.rstrip(":").lower()
    if not drive:
        return resolved.as_posix()
    remainder = resolved.as_posix().split(":", 1)[1]
    return f"/{drive}{remainder}"


def bash_path(path: pathlib.Path) -> str:
    return resolved_bash_path(path.resolve())


class GitHubApiRetryTests(unittest.TestCase):
    def test_posix_paths_are_returned_unchanged(self) -> None:
        self.assertEqual(
            resolved_bash_path(
                pathlib.PurePosixPath("/tmp/retry-tests/helper.sh"),
                platform="posix",
            ),
            "/tmp/retry-tests/helper.sh",
        )

    def run_helper(
        self,
        failures: list[str],
        *,
        partial_output: bool = False,
        api_args: str = '"repos/dotnet/skills/pulls/1201"',
    ) -> subprocess.CompletedProcess[str]:
        with tempfile.TemporaryDirectory() as temp:
            root = pathlib.Path(temp)
            bin_dir = root / "bin"
            bin_dir.mkdir()
            counter = root / "count"
            errors = root / "errors"
            errors.write_text("\n".join(failures), encoding="utf-8")
            fake_gh = bin_dir / "gh"
            fake_gh.write_text(
                textwrap.dedent(
                    """\
                    #!/usr/bin/env bash
                    set -euo pipefail
                    count=0
                    if [ -f "$GH_TEST_COUNTER" ]; then count=$(cat "$GH_TEST_COUNTER"); fi
                    count=$((count + 1))
                    printf '%s' "$count" > "$GH_TEST_COUNTER"
                    error=$(sed -n "${count}p" "$GH_TEST_ERRORS")
                    if [ -n "$error" ]; then
                      if [ "${GH_TEST_PARTIAL_OUTPUT:-false}" = "true" ]; then
                        printf '{"partial":true}'
                      fi
                      printf 'gh: %s\\n' "$error" >&2
                      exit 1
                    fi
                    printf '{"ok":true}'
                    """
                ),
                encoding="utf-8",
                newline="\n",
            )
            fake_gh.chmod(0o755)
            env = os.environ.copy()
            env.update(
                {
                    "GH_TEST_COUNTER": bash_path(counter),
                    "GH_TEST_ERRORS": bash_path(errors),
                    "GH_TEST_PARTIAL_OUTPUT": str(partial_output).lower(),
                    "GH_API_READ_MAX_ATTEMPTS": "3",
                    "GH_API_READ_BACKOFF_SECONDS": "0",
                }
            )
            result = subprocess.run(
                [
                    BASH,
                    "-c",
                    f'export PATH="{bash_path(bin_dir)}:$PATH"; '
                    f'source "{bash_path(HELPER)}"; '
                    f"gh_api_read {api_args}",
                ],
                cwd=REPO_ROOT,
                env=env,
                text=True,
                capture_output=True,
                check=False,
            )
            if not counter.exists():
                self.fail(
                    "fake gh was not invoked\n"
                    f"stdout:\n{result.stdout}\n"
                    f"stderr:\n{result.stderr}"
                )
            result.attempts = int(counter.read_text(encoding="utf-8"))
            return result

    def test_retries_http_500_then_succeeds(self) -> None:
        result = self.run_helper(
            ["Internal Server Error (HTTP 500)", "Bad Gateway (HTTP 502)"]
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.stdout, '{"ok":true}')
        self.assertEqual(result.attempts, 3)

    def test_retries_http_429_then_succeeds(self) -> None:
        result = self.run_helper(["Too Many Requests (HTTP 429)"])
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.attempts, 2)

    def test_retries_network_failure_then_succeeds(self) -> None:
        result = self.run_helper(
            ["dial tcp: lookup api.github.com: no such host"]
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.attempts, 2)

    def test_permanent_http_404_fails_without_retry(self) -> None:
        result = self.run_helper(["Not Found (HTTP 404)"])
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(result.attempts, 1)
        self.assertNotIn("retrying", result.stderr)

    def test_secondary_rate_limit_http_403_retries(self) -> None:
        result = self.run_helper(
            ["secondary rate limit exceeded (HTTP 403), Retry-After: 0"]
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.attempts, 2)

    def test_generic_http_403_fails_without_retry(self) -> None:
        result = self.run_helper(["Forbidden (HTTP 403)"])
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(result.attempts, 1)
        self.assertNotIn("retrying", result.stderr)

    def test_permanent_graphql_resolution_error_fails_without_retry(self) -> None:
        result = self.run_helper(
            ["Could not resolve to a Repository with the name 'missing/repo'."]
        )
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(result.attempts, 1)
        self.assertNotIn("retrying", result.stderr)

    def test_paginated_retry_emits_only_successful_attempt_output(self) -> None:
        result = self.run_helper(
            ["Bad Gateway (HTTP 502)"],
            partial_output=True,
            api_args='--paginate "repos/dotnet/skills/issues/1/comments"',
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.stdout, '{"ok":true}')
        self.assertEqual(result.attempts, 2)


if __name__ == "__main__":
    unittest.main()
