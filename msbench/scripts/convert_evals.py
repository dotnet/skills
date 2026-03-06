#!/usr/bin/env python3
"""
convert_evals.py — Convert eval.yaml scenarios to Harbor-format tasks for MSBench.

Usage:
    # Convert all eval.yaml files for all in-scope skills
    python msbench/scripts/convert_evals.py --skills-dir plugins/ --tests-dir tests/ --output-dir msbench/tasks/

    # Convert a single skill
    python msbench/scripts/convert_evals.py \
        --skills-dir plugins/dotnet/skills/analyzing-dotnet-performance \
        --tests-dir tests/dotnet/analyzing-dotnet-performance \
        --output-dir msbench/tasks/

    # Dry-run (show what would be generated)
    python msbench/scripts/convert_evals.py --skills-dir plugins/ --tests-dir tests/ --output-dir msbench/tasks/ --dry-run

    # Check mode (verify Harbor tasks are in sync with eval.yaml)
    python msbench/scripts/convert_evals.py --skills-dir plugins/ --tests-dir tests/ --output-dir msbench/tasks/ --check
"""

import argparse
import os
import re
import shutil
import sys
import textwrap
from pathlib import Path

import yaml

# Skills excluded from v1 (MCP-dependent or deferred)
EXCLUDED_SKILLS = {
    "binlog-failure-analysis",
    "build-perf-diagnostics",
    "build-parallelism",
    "dump-collect",
    "binlog-generation",
}

# Plugins to process
PLUGINS = ["dotnet", "dotnet-msbuild"]

# Default resource limits
DEFAULT_TIMEOUT = 600
DEFAULT_VERIFIER_TIMEOUT = 300
DEFAULT_BUILD_TIMEOUT = 300
DEFAULT_CPUS = 2
DEFAULT_MEMORY_MB = 4096
DEFAULT_STORAGE_MB = 10240


def slugify(name: str) -> str:
    """Convert a scenario name to a URL-friendly slug.

    Examples:
        'Detects compiled regex startup budget and regex chain allocations'
        -> 'compiled-regex-startup-budget'

        'Generate LibraryImport declaration from C header (.NET 8+)'
        -> 'libraryimport-from-c-header-net-8'
    """
    # Remove leading common verbs for brevity
    prefixes_to_strip = [
        r"^detects?\s+",
        r"^finds?\s+",
        r"^catches?\s+",
        r"^flags?\s+",
        r"^identifies?\s+",
        r"^diagnoses?\s+",
        r"^analyzes?\s+",
        r"^reviews?\s+",
        r"^modernizes?\s+",
        r"^establishes?\s+",
        r"^organizes?\s+",
        r"^investigates?\s+",
        r"^optimizes?\s+",
        r"^tests?\s+a\s+",
        r"^generates?\s+",
    ]

    slug = name.lower()
    for prefix in prefixes_to_strip:
        slug = re.sub(prefix, "", slug, flags=re.IGNORECASE)

    # Remove filler words for shorter slugs
    filler_words = [
        r"\band\b", r"\bthe\b", r"\bfor\b", r"\bwith\b", r"\bfrom\b",
        r"\bin\b", r"\bof\b", r"\bto\b", r"\bvia\b", r"\ba\b",
        r"\ban\b", r"\bthat\b", r"\bthis\b", r"\bis\b", r"\bare\b",
        r"\bonly\b", r"\bshould\b", r"\bnot\b",
    ]
    for word in filler_words:
        slug = re.sub(word, "", slug, flags=re.IGNORECASE)

    # Replace non-alphanumeric characters with hyphens
    slug = re.sub(r"[^a-z0-9]+", "-", slug)
    # Remove leading/trailing hyphens
    slug = slug.strip("-")
    # Collapse multiple hyphens
    slug = re.sub(r"-+", "-", slug)

    # Truncate to reasonable length (max 50 chars, on word boundary)
    if len(slug) > 50:
        slug = slug[:50].rsplit("-", 1)[0]

    return slug


def determine_difficulty(scenario: dict) -> str:
    """Determine task difficulty from scenario characteristics.

    Easy: <= 2 assertions and no rubric
    Hard: > 2 assertions or has rubric
    """
    assertions = scenario.get("assertions", [])
    rubric = scenario.get("rubric", [])

    # Count non-exit_success assertions
    real_assertions = [a for a in assertions if a.get("type") != "exit_success"]

    if len(real_assertions) <= 2 and not rubric:
        return "easy"
    return "hard"


def generate_task_toml(plugin: str, skill: str, scenario: dict, difficulty: str) -> str:
    """Generate task.toml content."""
    timeout = scenario.get("timeout", DEFAULT_TIMEOUT)
    tags_list = [
        "dotnet",
        f"skill:{skill}",
        f"plugin:{plugin}",
        "source:eval",
    ]
    if plugin == "dotnet-msbuild":
        tags_list.insert(1, "msbuild")

    tags_str = ", ".join(f'"{t}"' for t in tags_list)

    return textwrap.dedent(f"""\
        version = "1.0"

        [metadata]
        author_name = "dotnet/skills team"
        author_email = "dotnetskills@microsoft.com"
        category = "skills-evaluation"
        tags = [{tags_str}]
        difficulty = "{difficulty}"

        [resources]
        agent_timeout_sec = {timeout}
        verifier_timeout_sec = {DEFAULT_VERIFIER_TIMEOUT}
        environment_build_timeout_sec = {DEFAULT_BUILD_TIMEOUT}
        cpus = {DEFAULT_CPUS}
        memory_mb = {DEFAULT_MEMORY_MB}
        storage_mb = {DEFAULT_STORAGE_MB}
    """)


def generate_instruction_md(scenario: dict) -> str:
    """Generate instruction.md content from scenario prompt."""
    return scenario.get("prompt", "").strip() + "\n"


def generate_dockerfile(scenario: dict, task_name: str, has_fixtures: bool,
                        has_copy_test_files: bool, setup_commands: list) -> str:
    """Generate environment/Dockerfile content."""
    lines = [
        "FROM mcr.microsoft.com/dotnet/sdk:9.0",
        "",
        "# Install evaluation utilities",
        "RUN apt-get update && apt-get install -y --no-install-recommends \\",
        "    python3 python3-pip jq bc git curl procps \\",
        "    && rm -rf /var/lib/apt/lists/*",
        "",
        "# Create testbed directory",
        "RUN mkdir -p /testbed /output /app",
        "",
    ]

    if has_fixtures:
        lines.append("# Copy fixture files into testbed")
        lines.append("COPY fixtures/ /testbed/")
        lines.append("")

    if has_copy_test_files:
        lines.append("# Copy test project files into testbed")
        lines.append("COPY test_files/ /testbed/")
        lines.append("")

    # Add setup commands
    for cmd in setup_commands:
        lines.append(f"RUN {cmd}")

    lines.append("")
    lines.append("# Copy evaluation helpers")
    lines.append("COPY eval_helpers/ /app/")
    lines.append("")
    lines.append("WORKDIR /testbed")
    lines.append("")

    return "\n".join(lines)


def generate_test_sh(task_name: str, plugin: str, skill: str,
                     assertions: list, rubric: list, difficulty: str) -> str:
    """Generate tests/test.sh content from eval.yaml assertions."""
    lines = [
        "#!/bin/bash",
        "set -euo pipefail",
        "",
        "# Load assertion framework",
        "source /app/assertion_runner.sh",
        "",
        '# Read instance ID from metadata',
        'INSTANCE_ID=$(python3 -c "import json; print(json.load(open(\'/drop/metadata.json\'))[\'instance_id\'])")',
        "",
        "# Initialize assertion counters",
        "init_assertions",
        "",
        "# --- Deterministic Assertions ---",
        "",
    ]

    for assertion in assertions:
        atype = assertion.get("type", "")
        value = assertion.get("value", "")
        pattern = assertion.get("pattern", "")
        path = assertion.get("path", "")

        # Escape single quotes in values for bash
        value_escaped = value.replace("'", "'\\''") if value else ""
        pattern_escaped = pattern.replace("'", "'\\''") if pattern else ""
        path_escaped = path.replace("'", "'\\''") if path else ""

        if atype == "output_contains":
            lines.append(f"assert_output_contains '{value_escaped}'")
        elif atype == "output_not_contains":
            lines.append(f"assert_output_not_contains '{value_escaped}'")
        elif atype == "output_matches":
            lines.append(f"assert_output_matches '{pattern_escaped}'")
        elif atype == "output_not_matches":
            lines.append(f"assert_output_not_matches '{pattern_escaped}'")
        elif atype == "file_exists":
            lines.append(f"assert_file_exists '{path_escaped}'")
        elif atype == "file_not_exists":
            lines.append(f"assert_file_not_exists '{path_escaped}'")
        elif atype == "file_contains":
            lines.append(f"assert_file_contains '{path_escaped}' '{value_escaped}'")
        elif atype == "exit_success":
            lines.append("assert_exit_success")
        else:
            lines.append(f"# Unknown assertion type: {atype}")

    lines.append("")
    lines.append("# --- Finalize ---")
    lines.append("finalize_assertions")
    lines.append("")

    # Write eval.json
    lines.append("# Write evaluation results")
    lines.append("mkdir -p /output /logs/verifier")
    lines.append('echo "{\\"$INSTANCE_ID\\": {\\"resolved\\": $ALL_PASSED}}" > /output/eval.json')
    lines.append("")

    # Write reward file so parse.py (harbor-format-curation) preserves the result
    lines.append("# Write reward file for harbor-format-curation parse.py")
    lines.append('if [ "$ALL_PASSED" = "true" ]; then')
    lines.append('    echo "1.0" > /logs/verifier/reward.txt')
    lines.append("else")
    lines.append('    echo "0.0" > /logs/verifier/reward.txt')
    lines.append("fi")
    lines.append("")

    # Write custom_metrics.json
    rubric_items = "|".join(r.replace('"', '\\"') for r in rubric) if rubric else ""
    lines.append("# Write custom metrics")
    lines.append(f'python3 /app/write_eval.py "$INSTANCE_ID" "$ALL_PASSED" \\')
    lines.append(f"    --assertions-total $TOTAL_ASSERTIONS \\")
    lines.append(f"    --assertions-passed $PASSED_ASSERTIONS \\")
    lines.append(f'    --skill "{skill}" \\')
    lines.append(f'    --plugin "{plugin}"')
    lines.append("")

    return "\n".join(lines)


def _regex_exemplar(pattern: str) -> str:
    """Return a concrete string that matches a Perl-compatible regex pattern.

    This uses a simple heuristic: pick the first alternative in each group,
    strip anchors / quantifiers / character-class syntax, and collapse
    remaining meta-characters into literal text.
    """
    # Strip anchors
    s = re.sub(r"[\^$]", "", pattern)
    # Pick the first alternative in (...|...) groups
    s = re.sub(r"\(([^)]*)\)", lambda m: m.group(1).split("|")[0], s)
    # Unescape common escapes
    s = s.replace(r"\.", ".").replace(r"\s+", " ").replace(r"\s", " ")
    s = s.replace(r"\(", "(").replace(r"\)", ")")
    s = s.replace(r"\b", "").replace(r"\*", "*")
    s = s.replace(r"\{", "{").replace(r"\}", "}")
    # Remove residual quantifiers: ?, +, *, {n,m}
    s = re.sub(r"[?+*]|\{\d+,?\d*\}", "", s)
    # Remove residual brackets
    s = s.replace("[", "").replace("]", "")
    # Collapse repeated whitespace
    s = re.sub(r"\s+", " ", s).strip()
    return s


def generate_solve_sh(task_name: str, assertions: list) -> str:
    """Generate solution/solve.sh that satisfies the task's assertions.

    For output-based assertions the script writes keywords to
    /testbed/agent_output.txt.  For file-based assertions it creates the
    required files/content.  Negative assertions (output_not_matches,
    output_not_contains, file_not_exists) are inherently satisfied because
    the generated text only contains the positive keywords.
    """
    output_lines: list[str] = []  # text lines for agent_output.txt
    file_cmds: list[str] = []     # shell commands for file-based assertions

    for assertion in assertions:
        atype = assertion.get("type", "")
        value = assertion.get("value", "")
        pattern = assertion.get("pattern", "")
        path = assertion.get("path", "")

        if atype == "output_contains" and value:
            output_lines.append(value)
        elif atype == "output_matches" and pattern:
            exemplar = _regex_exemplar(pattern)
            if exemplar:
                output_lines.append(exemplar)
        elif atype == "file_exists" and path:
            # Create a minimal file that matches the glob
            concrete = path.replace("**/", "").replace("*", "oracle")
            file_cmds.append(f"mkdir -p /testbed/$(dirname '{concrete}')")
            file_cmds.append(f"touch /testbed/'{concrete}'")
        elif atype == "file_contains" and path and value:
            concrete = path.replace("**/", "").replace("*", "oracle")
            file_cmds.append(f"mkdir -p /testbed/$(dirname '{concrete}')")
            # Append the required content so grep finds it
            file_cmds.append(
                f"echo '{value}' >> /testbed/'{concrete}'"
            )
        # exit_success, output_not_matches, output_not_contains,
        # file_not_exists require no action — they are inherently
        # satisfied by not emitting problematic content.

    lines = [
        "#!/bin/bash",
        "set -euo pipefail",
        "cd /testbed",
        "",
        f"# Auto-generated oracle solution for: {task_name}",
        "# Produces output / files that satisfy the task's assertions.",
        "",
    ]

    if output_lines:
        lines.append("# Write expected keywords to agent_output.txt")
        lines.append("cat > /testbed/agent_output.txt << 'ORACLE_EOF'")
        for ol in output_lines:
            lines.append(ol)
        lines.append("ORACLE_EOF")
        lines.append("")

    for cmd in file_cmds:
        lines.append(cmd)

    if file_cmds:
        lines.append("")

    lines.append("exit 0")
    lines.append("")

    return "\n".join(lines)


def resolve_fixture_path(source: str, eval_yaml_dir: Path, repo_root: Path) -> Path:
    """Resolve a fixture source path relative to the eval.yaml location.

    eval.yaml fixture paths are relative to the SKILL.md location (where eval.yaml
    references them from). We need to resolve them to absolute paths.
    """
    # The source paths in eval.yaml are relative to the SKILL.md location
    # which is typically: plugins/{plugin}/skills/{skill}/SKILL.md
    # And the eval.yaml is at: tests/{plugin}/{skill}/eval.yaml
    # The source paths look like: ../../../../tests/dotnet/analyzing-dotnet-performance/fixtures/foo.cs

    # Try to resolve relative to the eval.yaml directory first
    resolved = (eval_yaml_dir / source).resolve()
    if resolved.exists():
        return resolved

    # Try resolving relative to repo root
    resolved = (repo_root / source.lstrip("./")).resolve()
    if resolved.exists():
        return resolved

    # Try stripping the relative prefix and resolving
    # Common pattern: ../../../../tests/dotnet/skill/fixtures/file.cs
    # Strip leading ../ segments
    clean_source = source
    while clean_source.startswith("../"):
        clean_source = clean_source[3:]
    resolved = (repo_root / clean_source).resolve()
    if resolved.exists():
        return resolved

    # Return what we computed even if it doesn't exist (for error reporting)
    return (eval_yaml_dir / source).resolve()


def process_scenario(
    plugin: str,
    skill: str,
    scenario: dict,
    eval_yaml_dir: Path,
    test_dir: Path,
    repo_root: Path,
    output_dir: Path,
    dry_run: bool = False,
) -> dict:
    """Process a single eval.yaml scenario and generate a Harbor task.

    Returns a dict with task metadata for reporting.
    """
    scenario_name = scenario.get("name", "unnamed")
    slug = slugify(scenario_name)
    task_name = f"{plugin}--{skill}--{slug}"
    task_dir = output_dir / task_name

    difficulty = determine_difficulty(scenario)
    setup = scenario.get("setup", {})
    assertions = scenario.get("assertions", [])
    rubric = scenario.get("rubric", [])

    # Determine fixture handling
    has_fixtures = False
    has_copy_test_files = False
    setup_commands = []
    fixture_files = {}  # dest_path -> source_path

    if setup:
        files = setup.get("files", [])
        for fentry in files:
            dest_path = fentry.get("path", "")
            source = fentry.get("source", "")
            content = fentry.get("content", "")

            if source:
                has_fixtures = True
                resolved = resolve_fixture_path(source, eval_yaml_dir, repo_root)
                fixture_files[dest_path] = resolved
            elif content:
                # Inline content — write to file during build
                setup_commands.append(
                    f"echo '{content}' > /testbed/{dest_path}"
                )

        if setup.get("copy_test_files", False):
            has_copy_test_files = True

        # Setup commands
        for cmd in setup.get("commands", []):
            setup_commands.append(cmd)

    result = {
        "task_name": task_name,
        "plugin": plugin,
        "skill": skill,
        "scenario_name": scenario_name,
        "difficulty": difficulty,
        "assertions_count": len(assertions),
        "rubric_count": len(rubric),
        "has_fixtures": has_fixtures,
        "has_copy_test_files": has_copy_test_files,
    }

    if dry_run:
        return result

    # Create task directory structure
    task_dir.mkdir(parents=True, exist_ok=True)
    (task_dir / "environment").mkdir(exist_ok=True)
    (task_dir / "tests").mkdir(exist_ok=True)
    (task_dir / "solution").mkdir(exist_ok=True)

    # Generate task.toml
    (task_dir / "task.toml").write_text(
        generate_task_toml(plugin, skill, scenario, difficulty),
        encoding="utf-8",
    )

    # Generate instruction.md
    (task_dir / "instruction.md").write_text(
        generate_instruction_md(scenario),
        encoding="utf-8",
    )

    # Copy fixture files into environment/ so they're in the Docker build context
    if has_fixtures:
        fixtures_dir = task_dir / "environment" / "fixtures"
        fixtures_dir.mkdir(exist_ok=True)
        for dest_name, source_path in fixture_files.items():
            dest_file = fixtures_dir / dest_name
            dest_file.parent.mkdir(parents=True, exist_ok=True)
            if source_path.exists():
                shutil.copy2(source_path, dest_file)
            else:
                print(f"  WARNING: Fixture not found: {source_path}", file=sys.stderr)
                dest_file.write_text(
                    f"# ERROR: Source fixture not found: {source_path}\n",
                    encoding="utf-8",
                )

    # Copy test files into environment/ so they're in the Docker build context
    if has_copy_test_files:
        test_files_dir = task_dir / "environment" / "test_files"
        test_files_dir.mkdir(exist_ok=True)
        if test_dir.exists():
            for item in test_dir.iterdir():
                # Skip eval.yaml and fixtures dir
                if item.name in ("eval.yaml", "fixtures", "__pycache__"):
                    continue
                dest = test_files_dir / item.name
                if item.is_dir():
                    shutil.copytree(item, dest, dirs_exist_ok=True)
                else:
                    shutil.copy2(item, dest)

    # Copy eval_helpers into environment/ so they're in the Docker build context
    eval_helpers_src = repo_root / "msbench" / "shared" / "eval_helpers"
    eval_helpers_dest = task_dir / "environment" / "eval_helpers"
    if eval_helpers_src.exists():
        shutil.copytree(eval_helpers_src, eval_helpers_dest, dirs_exist_ok=True)

    # Generate Dockerfile
    (task_dir / "environment" / "Dockerfile").write_text(
        generate_dockerfile(scenario, task_name, has_fixtures, has_copy_test_files,
                           setup_commands),
        encoding="utf-8",
    )

    # Generate test.sh
    test_sh_content = generate_test_sh(task_name, plugin, skill, assertions, rubric, difficulty)
    test_sh_path = task_dir / "tests" / "test.sh"
    test_sh_path.write_text(test_sh_content, encoding="utf-8")

    # Generate solve.sh from assertions
    (task_dir / "solution" / "solve.sh").write_text(
        generate_solve_sh(task_name, assertions),
        encoding="utf-8",
    )

    return result


def discover_skills(skills_dir: Path, tests_dir: Path, repo_root: Path) -> list:
    """Discover all in-scope skills with eval.yaml files.

    Returns list of (plugin, skill, eval_yaml_path, test_dir) tuples.
    """
    skills = []

    for plugin in PLUGINS:
        # Check if skills_dir points to a specific skill or to the plugins root
        if skills_dir.name in ("plugins",) or (skills_dir / plugin).exists():
            # Scan all skills in plugin
            plugin_skills_dir = skills_dir / plugin / "skills" if "plugins" in str(skills_dir) else skills_dir
            if not (skills_dir / plugin / "skills").exists() and "plugins" in str(skills_dir):
                continue
            plugin_skills_dir = skills_dir / plugin / "skills"

            for skill_dir in sorted(plugin_skills_dir.iterdir()):
                if not skill_dir.is_dir():
                    continue
                skill_name = skill_dir.name

                if skill_name in EXCLUDED_SKILLS:
                    continue

                eval_yaml = tests_dir / plugin / skill_name / "eval.yaml"
                test_dir = tests_dir / plugin / skill_name
                if eval_yaml.exists():
                    skills.append((plugin, skill_name, eval_yaml, test_dir))
        else:
            # Single skill mode — skills_dir points to a specific skill
            # Infer plugin and skill from path
            parts = skills_dir.parts
            for i, part in enumerate(parts):
                if part in PLUGINS:
                    plugin = part
                    # Skill name is the last component
                    skill_name = skills_dir.name
                    if skill_name in EXCLUDED_SKILLS:
                        break
                    eval_yaml = tests_dir / plugin / skill_name / "eval.yaml"
                    # Also try: tests_dir might point to the skill test dir directly
                    if not eval_yaml.exists() and tests_dir.name == skill_name:
                        eval_yaml = tests_dir / "eval.yaml"
                    test_dir = tests_dir if tests_dir.name == skill_name else tests_dir / plugin / skill_name
                    if eval_yaml.exists():
                        skills.append((plugin, skill_name, eval_yaml, test_dir))
                    break
            break  # Single skill mode — only process once

    return skills


def convert_all(
    skills_dir: Path,
    tests_dir: Path,
    output_dir: Path,
    repo_root: Path,
    dry_run: bool = False,
    check: bool = False,
) -> list:
    """Convert all in-scope eval.yaml scenarios to Harbor tasks.

    Returns list of task result dicts.
    """
    skills = discover_skills(skills_dir, tests_dir, repo_root)

    if not skills:
        print("No in-scope skills with eval.yaml files found.", file=sys.stderr)
        return []

    print(f"Found {len(skills)} in-scope skills with eval.yaml files")
    results = []
    errors = []

    for plugin, skill_name, eval_yaml_path, test_dir in skills:
        print(f"\nProcessing: {plugin}/{skill_name}")

        try:
            with open(eval_yaml_path, "r", encoding="utf-8") as f:
                eval_data = yaml.safe_load(f)
        except Exception as e:
            print(f"  ERROR: Failed to parse {eval_yaml_path}: {e}", file=sys.stderr)
            errors.append(f"{plugin}/{skill_name}: {e}")
            continue

        # Only convert evals that have opted in to msbench onboarding
        if not eval_data.get("msbench_ready", False):
            print(f"  SKIPPED: msbench_ready not set in {eval_yaml_path}")
            continue

        scenarios = eval_data.get("scenarios", [])
        if not scenarios:
            print(f"  WARNING: No scenarios found in {eval_yaml_path}")
            continue

        for scenario in scenarios:
            try:
                result = process_scenario(
                    plugin=plugin,
                    skill=skill_name,
                    scenario=scenario,
                    eval_yaml_dir=eval_yaml_path.parent,
                    test_dir=test_dir,
                    repo_root=repo_root,
                    output_dir=output_dir,
                    dry_run=dry_run,
                )
                results.append(result)
                action = "Would create" if dry_run else "Created"
                print(f"  {action}: {result['task_name']} (difficulty={result['difficulty']})")
            except Exception as e:
                error_msg = f"{plugin}/{skill_name}/{scenario.get('name', '?')}: {e}"
                print(f"  ERROR: {error_msg}", file=sys.stderr)
                errors.append(error_msg)

    # Summary
    print(f"\n{'=' * 60}")
    print(f"Summary: {len(results)} tasks {'would be ' if dry_run else ''}generated")
    print(f"  Easy: {sum(1 for r in results if r['difficulty'] == 'easy')}")
    print(f"  Hard: {sum(1 for r in results if r['difficulty'] == 'hard')}")
    if errors:
        print(f"  Errors: {len(errors)}")
        for err in errors:
            print(f"    - {err}")

    if check:
        # Verify that all expected tasks exist in output_dir
        existing_tasks = set()
        if output_dir.exists():
            existing_tasks = {d.name for d in output_dir.iterdir() if d.is_dir()}

        expected_tasks = {r["task_name"] for r in results}
        missing = expected_tasks - existing_tasks
        extra = existing_tasks - expected_tasks

        if missing:
            print(f"\n  DRIFT: {len(missing)} tasks missing from {output_dir}:")
            for t in sorted(missing):
                print(f"    - {t}")
        if extra:
            print(f"\n  DRIFT: {len(extra)} extra tasks in {output_dir}:")
            for t in sorted(extra):
                print(f"    - {t}")
        if not missing and not extra:
            print(f"\n  OK: All tasks in sync")

    return results


def main():
    parser = argparse.ArgumentParser(
        description="Convert eval.yaml scenarios to Harbor-format tasks for MSBench"
    )
    parser.add_argument(
        "--skills-dir",
        type=Path,
        required=True,
        help="Path to plugins/ directory (or specific skill directory)",
    )
    parser.add_argument(
        "--tests-dir",
        type=Path,
        required=True,
        help="Path to tests/ directory (or specific skill test directory)",
    )
    parser.add_argument(
        "--output-dir",
        type=Path,
        required=True,
        help="Output directory for Harbor tasks",
    )
    parser.add_argument(
        "--repo-root",
        type=Path,
        default=None,
        help="Repository root (auto-detected if not specified)",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Show what would be generated without creating files",
    )
    parser.add_argument(
        "--check",
        action="store_true",
        help="Check if Harbor tasks are in sync with eval.yaml (implies --dry-run)",
    )

    args = parser.parse_args()

    # Auto-detect repo root
    repo_root = args.repo_root
    if repo_root is None:
        # Walk up from skills_dir to find repo root (where plugins/ exists)
        candidate = args.skills_dir.resolve()
        while candidate != candidate.parent:
            if (candidate / "plugins").exists() or (candidate / "msbench").exists():
                repo_root = candidate
                break
            candidate = candidate.parent
        if repo_root is None:
            repo_root = Path.cwd()

    if args.check:
        args.dry_run = True

    results = convert_all(
        skills_dir=args.skills_dir.resolve(),
        tests_dir=args.tests_dir.resolve(),
        output_dir=args.output_dir.resolve(),
        repo_root=repo_root.resolve(),
        dry_run=args.dry_run,
        check=args.check,
    )

    if not results:
        sys.exit(1)

    sys.exit(0)


if __name__ == "__main__":
    main()
