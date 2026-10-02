from pathlib import Path
import hashlib
import os
import subprocess
import sys


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def read(path: str) -> str:
    return Path(path).read_text(encoding="utf-8")


def require_sha256(path: str, expected: str) -> None:
    content = Path(path).read_bytes().replace(b"\r\n", b"\n")
    actual = hashlib.sha256(content).hexdigest()
    require(actual == expected, f"Protected file changed: {path}")


def run(command: list[str], expected_output: str | None = None) -> None:
    env = os.environ.copy()
    env["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
    result = subprocess.run(
        command,
        check=False,
        encoding="utf-8",
        env=env,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
    )
    sys.stdout.write(result.stdout)
    require(result.returncode == 0, f"Command failed: {' '.join(command)}")
    if expected_output is not None:
        require(expected_output in result.stdout, f"Missing output: {expected_output}")


def reject_build_suppression() -> None:
    forbidden_names = {
        ".editorconfig",
        ".globalconfig",
        "Directory.Build.props",
        "Directory.Build.targets",
    }
    for path in Path(".").rglob("*"):
        if not path.is_file():
            continue
        require(path.name not in forbidden_names and path.suffix != ".rsp", f"Build policy override added: {path}")
    for project in Path(".").glob("*.csproj"):
        text = project.read_text(encoding="utf-8")
        require("NoWarn" not in text, f"Warnings suppressed in {project}")
        require("WarningsNotAsErrors" not in text, f"Warnings downgraded in {project}")
        require("<TreatWarningsAsErrors>false" not in text, f"Warnings-as-errors disabled in {project}")


def build_and_run(project: str, expected_output: str = "PASS") -> None:
    reject_build_suppression()
    run(["dotnet", "build", project, "--nologo", "--verbosity", "minimal", "-warnaserror"])
    run(["dotnet", "run", "--project", project, "--no-build"], expected_output)


def build(project: str) -> None:
    reject_build_suppression()
    run(["dotnet", "build", project, "--nologo", "--verbosity", "minimal", "-warnaserror"])
