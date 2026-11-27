import os
from pathlib import Path
import re
import subprocess
import sys
import xml.etree.ElementTree as ET


ORIGINAL = Path(".tcc-grade/original")
GENERATED = {".git", ".grading", ".tcc-grade", "bin", "obj", "TestResults"}
PASS_COUNT = re.compile(r"\b(?:passed|succeeded):\s*[1-9]\d*\b", re.IGNORECASE)


def files(root):
    result = set()
    for folder, dirs, filenames in os.walk(root):
        dirs[:] = [name for name in dirs if name not in GENERATED]
        result.update((Path(folder) / name).relative_to(root) for name in filenames)
    return result


def equivalent(first, second):
    return first.read_bytes().replace(b"\r\n", b"\n") == second.read_bytes().replace(b"\r\n", b"\n")


def solution_projects(solution):
    return {
        Path(project.attrib["Path"].replace("\\", "/"))
        for project in ET.parse(solution).iter("Project")
    }


def covering_projects(solution, production_project, expected_dir=None):
    production = production_project.resolve()
    for project in sorted(solution_projects(solution)):
        if project.suffix != ".csproj" or project == production_project or not project.is_file():
            continue
        if expected_dir is not None and project.parent != expected_dir:
            continue
        references = ET.parse(project).iter("ProjectReference")
        if any((project.parent / ref.get("Include", "").replace("\\", "/")).resolve() == production
               for ref in references):
            yield project


def passes_tests(target, *args):
    result = subprocess.run(["dotnet", "test", str(target), *args], capture_output=True, text=True, errors="replace")
    print(result.stdout, end="", flush=True)
    print(result.stderr, end="", file=sys.stderr, flush=True)
    return result.returncode == 0 and bool(PASS_COUNT.search(result.stdout))


def detects_change(projects, *args, env=None):
    targets = [part for project in projects for part in ("--tests", str(project))]
    command = ["node", ".grading/assert-tests-detect-change.js", *targets,
               "--restore", "src=.grading/original/src", *args]
    return bool(targets) and subprocess.run(command, env=env).returncode == 0


def registered(solution, original_solution, expected_dir=None):
    root = Path.cwd()
    original_projects = {project.resolve() for project in solution_projects(original_solution)}
    new_projects = {project.resolve() for project in solution_projects(solution)} - original_projects
    for candidate in sorted(new_projects):
        if not candidate.is_relative_to(root) or not candidate.is_file():
            continue
        if candidate.suffix not in {".csproj", ".fsproj", ".vbproj"}:
            continue
        project = candidate.relative_to(root)
        if expected_dir is not None and project.parent != expected_dir:
            continue
        if passes_tests(project) and detects_change([project]):
            return True
    return False


def catalog_tests(solution):
    projects = list(covering_projects(solution, Path("src/Catalog/Catalog.csproj")))
    browser_filter = ("--filter", "Category!=Browser")
    if passes_tests(solution, *browser_filter) and detects_change(projects, *browser_filter):
        return True
    if not passes_tests(solution):
        return False

    env = os.environ.copy()
    browser_path = env.get("PLAYWRIGHT_BROWSERS_PATH")
    if browser_path and browser_path != "0":
        env["PLAYWRIGHT_BROWSERS_PATH"] = str(Path(browser_path).resolve())
    else:
        # A browser installed package-locally is in bin/, which the rollback
        # clone intentionally omits. Use it without copying the installation.
        local_browsers = next((
            path for project in projects
            for path in project.parent.glob("bin/*/*/.playwright/package/.local-browsers")
        ), None)
        if local_browsers:
            env["PLAYWRIGHT_BROWSERS_PATH"] = str(local_browsers.resolve())
    return detects_change(projects, env=env)


def preserves_other_files(workspace, edited):
    original = files(ORIGINAL)
    actual = files(workspace)
    if actual != original:
        print("Scope changed:", ", ".join(sorted(path.as_posix() for path in actual ^ original)))
        return False
    for path in original - {edited}:
        if not equivalent(workspace / path, ORIGINAL / path):
            print(f"Unexpected edit: {path}")
            return False
    allowed_projects = {workspace / path for path in original}
    return all(
        path in allowed_projects for path in files(Path("."))
        if path.suffix in {".csproj", ".fsproj", ".vbproj"} and ".agents" not in path.parts
    )


def documents_verbose(workspace):
    readme = Path("README.md")
    return preserves_other_files(workspace, readme) and "--verbose" in (workspace / readme).read_text(encoding="utf-8")


def updates_runtime(workspace):
    dockerfile = Path("Dockerfile")
    if not preserves_other_files(workspace, dockerfile):
        return False
    old_base = ["FROM", "mcr.microsoft.com/dotnet/runtime:10.0"]
    new_base = ["FROM", "mcr.microsoft.com/dotnet/runtime:10.0-noble"]
    original = (ORIGINAL / dockerfile).read_text(encoding="utf-8").splitlines()
    actual = (workspace / dockerfile).read_text(encoding="utf-8").splitlines()
    expected = [new_base if line.split() == old_base else line.split() for line in original]
    return [line.split() for line in actual] == expected


if __name__ == "__main__":
    handlers = {
        "registered": registered,
        "catalog-tests": catalog_tests,
        "documents-verbose": documents_verbose,
        "updates-runtime": updates_runtime,
    }
    operation, *paths = sys.argv[1:]
    if not handlers[operation](*map(Path, paths)):
        sys.exit(f"{operation}: contract not satisfied")
