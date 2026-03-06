#!/usr/bin/env python3
"""
analyze_results.py — Post-run A/B analysis for dotnetskills MSBench benchmark.

Computes resolve rate delta between with-skills and without-skills runs.
Reports per-task, per-skill, and overall metrics.

Usage:
    python msbench/scripts/analyze_results.py --with-skills results_with.json --without-skills results_without.json
    python msbench/scripts/analyze_results.py --results-dir ./results/
"""

import argparse
import json
import os
import sys
from collections import defaultdict
from pathlib import Path


def load_results(filepath: str) -> dict:
    """Load eval.json results from a file or directory."""
    results = {}
    path = Path(filepath)

    if path.is_file():
        with open(path, "r") as f:
            data = json.load(f)
        results.update(data)
    elif path.is_dir():
        # Scan for eval.json files in directory
        for eval_json in path.rglob("eval.json"):
            with open(eval_json, "r") as f:
                data = json.load(f)
            results.update(data)
    else:
        print(f"Error: {filepath} not found", file=sys.stderr)
        sys.exit(1)

    return results


def extract_task_metadata(instance_id: str) -> dict:
    """Extract plugin, skill, and scenario from task instance_id.

    Format: {plugin}--{skill}--{scenario-slug}
    """
    parts = instance_id.split("--")
    if len(parts) >= 3:
        return {
            "plugin": parts[0],
            "skill": parts[1],
            "scenario": "--".join(parts[2:]),
        }
    return {"plugin": "unknown", "skill": "unknown", "scenario": instance_id}


def compute_metrics(with_skills: dict, without_skills: dict) -> dict:
    """Compute A/B comparison metrics."""
    # Find common tasks
    common_tasks = set(with_skills.keys()) & set(without_skills.keys())

    if not common_tasks:
        return {"error": "No common tasks between the two runs"}

    # Per-task results
    per_task = []
    for task_id in sorted(common_tasks):
        ws_resolved = with_skills[task_id].get("resolved", False)
        wo_resolved = without_skills[task_id].get("resolved", False)
        meta = extract_task_metadata(task_id)

        per_task.append({
            "instance_id": task_id,
            "plugin": meta["plugin"],
            "skill": meta["skill"],
            "with_skills_resolved": ws_resolved,
            "without_skills_resolved": wo_resolved,
            "delta": int(ws_resolved) - int(wo_resolved),
        })

    # Per-skill aggregation
    skill_groups = defaultdict(list)
    for task in per_task:
        key = f"{task['plugin']}/{task['skill']}"
        skill_groups[key].append(task)

    per_skill = {}
    for skill_key, tasks in sorted(skill_groups.items()):
        ws_count = sum(1 for t in tasks if t["with_skills_resolved"])
        wo_count = sum(1 for t in tasks if t["without_skills_resolved"])
        n = len(tasks)
        per_skill[skill_key] = {
            "count": n,
            "with_skills_resolved": ws_count,
            "without_skills_resolved": wo_count,
            "with_skills_rate": ws_count / n if n > 0 else 0,
            "without_skills_rate": wo_count / n if n > 0 else 0,
            "delta_rate": (ws_count - wo_count) / n if n > 0 else 0,
        }

    # Overall
    total = len(common_tasks)
    ws_total = sum(1 for t in per_task if t["with_skills_resolved"])
    wo_total = sum(1 for t in per_task if t["without_skills_resolved"])

    overall = {
        "total_tasks": total,
        "with_skills_resolved": ws_total,
        "without_skills_resolved": wo_total,
        "with_skills_rate": ws_total / total if total > 0 else 0,
        "without_skills_rate": wo_total / total if total > 0 else 0,
        "delta_resolve_rate": (ws_total - wo_total) / total if total > 0 else 0,
        "skills_with_positive_delta": sum(1 for s in per_skill.values() if s["delta_rate"] > 0),
        "skills_with_zero_delta": sum(1 for s in per_skill.values() if s["delta_rate"] == 0),
        "skills_with_negative_delta": sum(1 for s in per_skill.values() if s["delta_rate"] < 0),
    }

    # Only in one run
    only_with = set(with_skills.keys()) - set(without_skills.keys())
    only_without = set(without_skills.keys()) - set(with_skills.keys())

    return {
        "overall": overall,
        "per_skill": per_skill,
        "per_task": per_task,
        "only_in_with_skills": sorted(only_with),
        "only_in_without_skills": sorted(only_without),
    }


def format_report(metrics: dict) -> str:
    """Format metrics as a human-readable report."""
    lines = []
    lines.append("=" * 70)
    lines.append("MSBench dotnetskills A/B Comparison Report")
    lines.append("=" * 70)

    if "error" in metrics:
        lines.append(f"\nError: {metrics['error']}")
        return "\n".join(lines)

    overall = metrics["overall"]
    lines.append(f"\n{'OVERALL':=^70}")
    lines.append(f"  Total tasks compared:     {overall['total_tasks']}")
    lines.append(f"  With-skills resolved:     {overall['with_skills_resolved']}/{overall['total_tasks']} ({overall['with_skills_rate']:.1%})")
    lines.append(f"  Without-skills resolved:  {overall['without_skills_resolved']}/{overall['total_tasks']} ({overall['without_skills_rate']:.1%})")
    lines.append(f"  Delta (resolve rate):     {overall['delta_resolve_rate']:+.1%}")
    lines.append(f"  Skills with positive Δ:   {overall['skills_with_positive_delta']}")
    lines.append(f"  Skills with zero Δ:       {overall['skills_with_zero_delta']}")
    lines.append(f"  Skills with negative Δ:   {overall['skills_with_negative_delta']}")

    lines.append(f"\n{'PER-SKILL BREAKDOWN':=^70}")
    lines.append(f"  {'Skill':<45} {'With':>6} {'W/o':>6} {'Δ':>8}")
    lines.append(f"  {'-' * 45} {'-' * 6} {'-' * 6} {'-' * 8}")
    for skill, data in sorted(metrics["per_skill"].items()):
        ws = f"{data['with_skills_rate']:.0%}"
        wo = f"{data['without_skills_rate']:.0%}"
        delta = f"{data['delta_rate']:+.0%}"
        lines.append(f"  {skill:<45} {ws:>6} {wo:>6} {delta:>8}")

    lines.append(f"\n{'PER-TASK DETAILS':=^70}")
    lines.append(f"  {'Task ID':<55} {'W/S':>5} {'W/o':>5} {'Δ':>3}")
    lines.append(f"  {'-' * 55} {'-' * 5} {'-' * 5} {'-' * 3}")
    for task in metrics["per_task"]:
        # Shorten task ID for display
        tid = task["instance_id"]
        if len(tid) > 55:
            tid = tid[:52] + "..."
        ws = "✓" if task["with_skills_resolved"] else "✗"
        wo = "✓" if task["without_skills_resolved"] else "✗"
        delta = f"{task['delta']:+d}" if task["delta"] != 0 else " 0"
        lines.append(f"  {tid:<55} {ws:>5} {wo:>5} {delta:>3}")

    if metrics["only_in_with_skills"]:
        lines.append(f"\nTasks only in with-skills run: {len(metrics['only_in_with_skills'])}")
    if metrics["only_in_without_skills"]:
        lines.append(f"Tasks only in without-skills run: {len(metrics['only_in_without_skills'])}")

    lines.append("")
    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser(
        description="Analyze dotnetskills MSBench A/B comparison results"
    )
    parser.add_argument(
        "--with-skills",
        type=str,
        help="Path to with-skills eval.json results (file or directory)",
    )
    parser.add_argument(
        "--without-skills",
        type=str,
        help="Path to without-skills eval.json results (file or directory)",
    )
    parser.add_argument(
        "--output",
        type=str,
        help="Output JSON file for metrics (optional)",
    )
    parser.add_argument(
        "--format",
        choices=["text", "json", "both"],
        default="both",
        help="Output format (default: both)",
    )

    args = parser.parse_args()

    if not args.with_skills or not args.without_skills:
        parser.error("Both --with-skills and --without-skills are required")

    with_skills = load_results(args.with_skills)
    without_skills = load_results(args.without_skills)

    print(f"Loaded {len(with_skills)} with-skills results")
    print(f"Loaded {len(without_skills)} without-skills results")

    metrics = compute_metrics(with_skills, without_skills)

    if args.format in ("text", "both"):
        print(format_report(metrics))

    if args.format in ("json", "both") and args.output:
        with open(args.output, "w") as f:
            json.dump(metrics, f, indent=2)
        print(f"Metrics written to {args.output}")


if __name__ == "__main__":
    main()
