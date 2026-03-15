#!/usr/bin/env python3
"""Generate msbench dataset.jsonl from Harbor-format tasks.

Reads every task directory under msbench/tasks/, extracts metadata from
task.toml and instruction.md, and writes a JSONL dataset file that can be
passed to ``msbench-cli run --dataset <path>``.

Usage:
    python msbench/scripts/generate_dataset.py [--tasks-dir msbench/tasks/] [--output msbench/dataset.jsonl] [--check]
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

try:
    import tomllib  # Python 3.11+
except ModuleNotFoundError:
    try:
        import tomli as tomllib  # type: ignore[no-redef]
    except ModuleNotFoundError:
        tomllib = None  # type: ignore[assignment]


def _parse_toml(path: Path) -> dict:
    """Parse a TOML file, with a regex fallback if no TOML library is available."""
    if tomllib is not None:
        return tomllib.loads(path.read_text(encoding="utf-8"))

    # Minimal regex-based fallback for the simple task.toml structure
    import re

    text = path.read_text(encoding="utf-8")
    data: dict = {}
    current_section: dict = data
    for line in text.splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        section_match = re.match(r"^\[(.+)]$", line)
        if section_match:
            parts = section_match.group(1).split(".")
            current_section = data
            for part in parts:
                current_section = current_section.setdefault(part, {})
            continue
        kv_match = re.match(r'^(\w+)\s*=\s*(.+)$', line)
        if kv_match:
            key, value = kv_match.group(1), kv_match.group(2).strip()
            if value.startswith('"') and value.endswith('"'):
                value = value[1:-1]
            elif value.startswith("[") and value.endswith("]"):
                value = [v.strip().strip('"') for v in value[1:-1].split(",")]
            elif value.isdigit():
                value = int(value)
            current_section[key] = value
    return data


def _read_version(tasks_dir: Path) -> str:
    """Read the benchmark version from version.txt."""
    version_file = tasks_dir.parent / "version.txt"
    if version_file.exists():
        return version_file.read_text(encoding="utf-8").strip()
    return "0.1.0"


def _read_dotnetskills_toml(tasks_dir: Path) -> dict:
    """Read benchmark name and registry info from dotnetskills.toml."""
    toml_file = tasks_dir.parent / "dotnetskills.toml"
    if toml_file.exists():
        data = _parse_toml(toml_file)
        return {
            "benchmark": data.get("import", {}).get("dataset", "dotnetskills"),
            "registry": data.get("docker", {}).get("prod", {}).get("registry", "codeexecservice.azurecr.io"),
        }
    return {"benchmark": "dotnetskills", "registry": "codeexecservice.azurecr.io"}


BENCHMARK_COLUMNS = "benchmark,instance_id,problem_statement,image_tag,benchmark_columns,is_harbor_task,difficulty"

# Map plugin prefix to benchmark suffix
PLUGIN_BENCHMARK_SUFFIX = {
    "dotnet-msbuild": "msbuild",
    "dotnet": "dotnet",
}


def _plugin_from_instance_id(instance_id: str) -> str:
    """Extract the plugin name from a task instance_id.

    Instance IDs follow the pattern ``{plugin}--{skill}--{slug}``.
    We match the longest plugin prefix first (e.g. ``dotnet-msbuild``
    before ``dotnet``).
    """
    for prefix in sorted(PLUGIN_BENCHMARK_SUFFIX, key=len, reverse=True):
        if instance_id.startswith(prefix + "--"):
            return prefix
    return "dotnet"


def generate_dataset(tasks_dir: Path, *, unified: bool = False) -> list[dict]:
    """Generate dataset rows from all task directories.

    When *unified* is True, all tasks share a single benchmark name
    (``dotnetskills``).  Otherwise each task gets a per-plugin benchmark
    name such as ``dotnetskills-dotnet`` or ``dotnetskills-msbuild``.
    """
    version = _read_version(tasks_dir)
    config = _read_dotnetskills_toml(tasks_dir)
    base_benchmark = config["benchmark"]

    rows: list[dict] = []
    for task_dir in sorted(tasks_dir.iterdir()):
        if not task_dir.is_dir():
            continue

        task_toml = task_dir / "task.toml"
        instruction_md = task_dir / "instruction.md"

        if not task_toml.exists() or not instruction_md.exists():
            print(f"WARN: skipping {task_dir.name} (missing task.toml or instruction.md)", file=sys.stderr)
            continue

        toml_data = _parse_toml(task_toml)
        problem_statement = instruction_md.read_text(encoding="utf-8").strip()
        instance_id = task_dir.name
        difficulty = toml_data.get("metadata", {}).get("difficulty", "unknown")

        image_tag = f"{base_benchmark}.eval.x86_64.{instance_id}:msbench-{version}"

        if unified:
            benchmark = base_benchmark
        else:
            plugin = _plugin_from_instance_id(instance_id)
            suffix = PLUGIN_BENCHMARK_SUFFIX.get(plugin, plugin)
            benchmark = f"{base_benchmark}-{suffix}"

        # benchmark is the first key for compatibility with msbench-cli
        row = {
            "benchmark": benchmark,
            "instance_id": instance_id,
            "image_tag": image_tag,
            "benchmark_columns": BENCHMARK_COLUMNS,
            "problem_statement": problem_statement,
            "is_harbor_task": True,
            "difficulty": difficulty,
        }
        rows.append(row)

    return rows


def write_jsonl(rows: list[dict], output: Path) -> None:
    """Write rows to a JSONL file."""
    with open(output, "w", encoding="utf-8", newline="\n") as f:
        for row in rows:
            f.write(json.dumps(row, ensure_ascii=False) + "\n")


def main() -> int:
    parser = argparse.ArgumentParser(description="Generate msbench dataset.jsonl from Harbor-format tasks")
    parser.add_argument("--tasks-dir", default="msbench/tasks", help="Path to tasks directory")
    parser.add_argument("--output", default="msbench/dataset.jsonl", help="Output JSONL file path")
    parser.add_argument("--unified", action="store_true",
                        help="Use a single benchmark name for all tasks (no per-plugin split)")
    parser.add_argument("--check", action="store_true", help="Verify existing dataset is in sync; exit non-zero on drift")
    args = parser.parse_args()

    tasks_dir = Path(args.tasks_dir)
    output = Path(args.output)

    if not tasks_dir.exists():
        print(f"ERROR: tasks directory not found: {tasks_dir}", file=sys.stderr)
        return 1

    rows = generate_dataset(tasks_dir, unified=args.unified)

    if not rows:
        print("ERROR: no tasks found", file=sys.stderr)
        return 1

    if args.check:
        if not output.exists():
            print(f"ERROR: dataset file not found: {output}", file=sys.stderr)
            return 1
        expected = "".join(json.dumps(r, ensure_ascii=False) + "\n" for r in rows)
        actual = output.read_text(encoding="utf-8")
        if expected != actual:
            print(f"ERROR: dataset is out of sync with tasks. Regenerate with:", file=sys.stderr)
            print(f"  python msbench/scripts/generate_dataset.py", file=sys.stderr)
            return 1
        print(f"OK: dataset ({len(rows)} instances) is in sync")
        return 0

    write_jsonl(rows, output)
    print(f"Generated {output} with {len(rows)} instances")
    return 0


if __name__ == "__main__":
    sys.exit(main())
