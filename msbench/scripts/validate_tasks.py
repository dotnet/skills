#!/usr/bin/env python3
"""
validate_tasks.py — End-to-end validation for generated Harbor tasks.

Validates:
1. All expected task directories exist
2. Each task has required files (task.toml, instruction.md, Dockerfile, test.sh, solve.sh)
3. task.toml has valid structure
4. instruction.md is non-empty
5. Dockerfile follows expected patterns
6. test.sh sources assertion_runner.sh and has correct structure
7. eval_helpers are present in each task
8. Fixture/test_files are present where expected
9. File encoding is UTF-8
10. Cross-reference: each task maps to an eval.yaml scenario

Usage:
    python msbench/scripts/validate_tasks.py --tasks-dir msbench/tasks/ --tests-dir tests/ --skills-dir plugins/
"""

import argparse
import os
import re
import sys
from pathlib import Path

import yaml


class ValidationResult:
    """Tracks validation results."""

    def __init__(self):
        self.passed = 0
        self.failed = 0
        self.warnings = 0
        self.errors = []
        self.warning_msgs = []

    def check(self, condition: bool, message: str, task: str = "") -> bool:
        prefix = f"[{task}] " if task else ""
        if condition:
            self.passed += 1
            return True
        else:
            self.failed += 1
            self.errors.append(f"{prefix}{message}")
            return False

    def warn(self, message: str, task: str = ""):
        prefix = f"[{task}] " if task else ""
        self.warnings += 1
        self.warning_msgs.append(f"{prefix}{message}")

    def summary(self) -> str:
        lines = []
        lines.append("=" * 70)
        lines.append("VALIDATION SUMMARY")
        lines.append("=" * 70)
        lines.append(f"  Checks passed:  {self.passed}")
        lines.append(f"  Checks failed:  {self.failed}")
        lines.append(f"  Warnings:       {self.warnings}")
        lines.append(f"  Overall:        {'PASS' if self.failed == 0 else 'FAIL'}")

        if self.errors:
            lines.append(f"\nERRORS ({len(self.errors)}):")
            for err in self.errors:
                lines.append(f"  ✗ {err}")

        if self.warning_msgs:
            lines.append(f"\nWARNINGS ({len(self.warning_msgs)}):")
            for warn in self.warning_msgs:
                lines.append(f"  ⚠ {warn}")

        lines.append("")
        return "\n".join(lines)


def validate_task_directory(task_dir: Path, result: ValidationResult) -> None:
    """Validate a single task directory."""
    task_name = task_dir.name

    # --- Required files ---
    required_files = {
        "task.toml": task_dir / "task.toml",
        "instruction.md": task_dir / "instruction.md",
        "Dockerfile": task_dir / "environment" / "Dockerfile",
        "test.sh": task_dir / "tests" / "test.sh",
        "solve.sh": task_dir / "solution" / "solve.sh",
    }

    for name, path in required_files.items():
        result.check(path.exists(), f"Missing required file: {name}", task_name)

    # --- Required directories ---
    for dirname in ["environment", "tests", "solution"]:
        result.check(
            (task_dir / dirname).exists(),
            f"Missing required directory: {dirname}",
            task_name,
        )
    # eval_helpers must be inside environment/ for Docker build context
    result.check(
        (task_dir / "environment" / "eval_helpers").exists(),
        "Missing required directory: environment/eval_helpers",
        task_name,
    )

    # --- task.toml validation ---
    toml_path = task_dir / "task.toml"
    if toml_path.exists():
        content = toml_path.read_text(encoding="utf-8")
        result.check('version = "1.0"' in content, "task.toml missing version", task_name)
        result.check("[metadata]" in content, "task.toml missing [metadata]", task_name)
        result.check("[resources]" in content, "task.toml missing [resources]", task_name)
        result.check("author_name" in content, "task.toml missing author_name", task_name)
        result.check("difficulty" in content, "task.toml missing difficulty", task_name)
        result.check("tags" in content, "task.toml missing tags", task_name)
        result.check("agent_timeout_sec" in content, "task.toml missing agent_timeout_sec", task_name)

        # Validate tags format
        tags_match = re.search(r'tags\s*=\s*\[(.+)\]', content)
        if result.check(tags_match is not None, "task.toml tags malformed", task_name):
            tags_str = tags_match.group(1)
            result.check('"dotnet"' in tags_str, "task.toml tags missing 'dotnet'", task_name)
            result.check('"skill:' in tags_str, "task.toml tags missing skill tag", task_name)
            result.check('"plugin:' in tags_str, "task.toml tags missing plugin tag", task_name)
            result.check('"source:eval"' in tags_str, "task.toml tags missing source tag", task_name)

    # --- instruction.md validation ---
    instruction_path = task_dir / "instruction.md"
    if instruction_path.exists():
        content = instruction_path.read_text(encoding="utf-8")
        result.check(len(content.strip()) > 10, "instruction.md is too short", task_name)

    # --- Dockerfile validation ---
    dockerfile_path = task_dir / "environment" / "Dockerfile"
    if dockerfile_path.exists():
        content = dockerfile_path.read_text(encoding="utf-8")
        result.check(
            "FROM mcr.microsoft.com/dotnet/sdk:9.0" in content,
            "Dockerfile missing base image",
            task_name,
        )
        result.check("python3" in content, "Dockerfile missing python3 install", task_name)
        result.check(
            "COPY eval_helpers/ /app/" in content,
            "Dockerfile missing eval_helpers COPY",
            task_name,
        )

        # Check that fixtures/test_files are copied if they exist
        # These must be inside environment/ for the Docker build context
        if (task_dir / "environment" / "fixtures").exists():
            result.check(
                "COPY fixtures/ /testbed/" in content,
                "Dockerfile missing COPY fixtures/ (environment/fixtures dir exists)",
                task_name,
            )
        if (task_dir / "environment" / "test_files").exists():
            result.check(
                "COPY test_files/ /testbed/" in content,
                "Dockerfile missing COPY test_files/ (environment/test_files dir exists)",
                task_name,
            )

    # --- test.sh validation ---
    test_sh_path = task_dir / "tests" / "test.sh"
    if test_sh_path.exists():
        content = test_sh_path.read_text(encoding="utf-8")
        result.check(content.startswith("#!/bin/bash"), "test.sh missing shebang", task_name)
        result.check(
            "source /app/assertion_runner.sh" in content,
            "test.sh missing assertion_runner.sh source",
            task_name,
        )
        result.check(
            "init_assertions" in content,
            "test.sh missing init_assertions call",
            task_name,
        )
        result.check(
            "finalize_assertions" in content,
            "test.sh missing finalize_assertions call",
            task_name,
        )
        result.check(
            "eval.json" in content,
            "test.sh missing eval.json output",
            task_name,
        )
        result.check(
            "write_eval.py" in content,
            "test.sh missing write_eval.py call",
            task_name,
        )

        # Check for at least one assertion call
        assertion_pattern = re.compile(r"assert_\w+")
        assertions = assertion_pattern.findall(content)
        result.check(
            len(assertions) > 0,
            "test.sh has no assertion calls",
            task_name,
        )

    # --- solve.sh validation ---
    solve_sh_path = task_dir / "solution" / "solve.sh"
    if solve_sh_path.exists():
        content = solve_sh_path.read_text(encoding="utf-8")
        result.check(content.startswith("#!/bin/bash"), "solve.sh missing shebang", task_name)

    # --- eval_helpers validation ---
    helpers_dir = task_dir / "environment" / "eval_helpers"
    if helpers_dir.exists():
        for helper in ["assertion_runner.sh", "write_eval.py"]:
            result.check(
                (helpers_dir / helper).exists(),
                f"environment/eval_helpers missing {helper}",
                task_name,
            )

    # --- Task naming convention ---
    parts = task_name.split("--")
    result.check(
        len(parts) >= 3,
        f"Task name doesn't follow {{plugin}}--{{skill}}--{{slug}} format: {task_name}",
        task_name,
    )
    if len(parts) >= 3:
        plugin = parts[0]
        result.check(
            plugin in ("dotnet", "dotnet-msbuild"),
            f"Unknown plugin in task name: {plugin}",
            task_name,
        )


def validate_cross_reference(
    tasks_dir: Path, tests_dir: Path, skills_dir: Path, result: ValidationResult
) -> None:
    """Validate that generated tasks match eval.yaml scenarios."""
    # Collect all task names
    task_names = {d.name for d in tasks_dir.iterdir() if d.is_dir()}

    # Collect all expected tasks from eval.yaml files
    excluded_skills = {
        "binlog-failure-analysis", "binlog-generation",
        "build-perf-diagnostics", "build-parallelism", "dump-collect",
    }
    plugins = ["dotnet", "dotnet-msbuild"]

    expected_count = 0
    for plugin in plugins:
        plugin_test_dir = tests_dir / plugin
        if not plugin_test_dir.exists():
            continue

        for skill_dir in sorted(plugin_test_dir.iterdir()):
            if not skill_dir.is_dir() or skill_dir.name in excluded_skills:
                continue

            eval_yaml = skill_dir / "eval.yaml"
            if not eval_yaml.exists():
                continue

            # Check that plugin has SKILL.md
            skill_md = skills_dir / plugin / "skills" / skill_dir.name / "SKILL.md"
            if not skill_md.exists():
                result.warn(
                    f"eval.yaml exists for {plugin}/{skill_dir.name} but no SKILL.md found",
                )
                continue

            try:
                with open(eval_yaml, "r", encoding="utf-8") as f:
                    data = yaml.safe_load(f)

                # Only count evals that have opted in to msbench onboarding
                if not data.get("msbench_ready", False):
                    continue

                scenarios = data.get("scenarios", [])
                expected_count += len(scenarios)
            except Exception as e:
                result.warn(f"Failed to parse {eval_yaml}: {e}")

    result.check(
        len(task_names) == expected_count,
        f"Task count mismatch: {len(task_names)} tasks generated vs {expected_count} expected from eval.yaml",
    )

    # Check no unexpected tasks
    for task_name in task_names:
        parts = task_name.split("--")
        if len(parts) >= 3:
            plugin = parts[0]
            skill = parts[1]
            result.check(
                skill not in excluded_skills,
                f"Task generated for excluded skill: {task_name}",
            )


def validate_all(tasks_dir: Path, tests_dir: Path = None, skills_dir: Path = None) -> ValidationResult:
    """Run all validations."""
    result = ValidationResult()

    # Validate tasks directory exists
    if not result.check(tasks_dir.exists(), f"Tasks directory not found: {tasks_dir}"):
        return result

    task_dirs = sorted([d for d in tasks_dir.iterdir() if d.is_dir()])
    result.check(len(task_dirs) > 0, "No task directories found")

    print(f"Validating {len(task_dirs)} tasks in {tasks_dir}...")
    print()

    for task_dir in task_dirs:
        validate_task_directory(task_dir, result)

    # Cross-reference validation
    if tests_dir and skills_dir and tests_dir.exists() and skills_dir.exists():
        print("Running cross-reference validation...")
        validate_cross_reference(tasks_dir, tests_dir, skills_dir, result)

    return result


def main():
    parser = argparse.ArgumentParser(description="Validate generated Harbor tasks")
    parser.add_argument(
        "--tasks-dir", type=Path, required=True, help="Path to generated tasks directory"
    )
    parser.add_argument(
        "--tests-dir", type=Path, default=None, help="Path to tests/ directory for cross-reference"
    )
    parser.add_argument(
        "--skills-dir", type=Path, default=None, help="Path to plugins/ directory for cross-reference"
    )

    args = parser.parse_args()

    result = validate_all(args.tasks_dir, args.tests_dir, args.skills_dir)
    print(result.summary())

    sys.exit(0 if result.failed == 0 else 1)


if __name__ == "__main__":
    main()
