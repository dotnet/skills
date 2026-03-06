#!/usr/bin/env python3
"""write_eval.py — Generate /output/eval.json and /output/custom_metrics.json."""

import json
import os
import sys


def write_eval_json(instance_id: str, resolved: bool, output_dir: str = "/output"):
    """Write the standard MSBench eval.json result file."""
    os.makedirs(output_dir, exist_ok=True)
    result = {instance_id: {"resolved": resolved}}
    with open(os.path.join(output_dir, "eval.json"), "w") as f:
        json.dump(result, f, indent=2)
    print(f"Wrote eval.json: {instance_id} -> resolved={resolved}")


def write_custom_metrics(
    instance_id: str,
    build_success: bool,
    assertions_total: int,
    assertions_passed: int,
    skill_name: str = "",
    plugin_name: str = "",
    source_type: str = "eval",
    difficulty: str = "easy",
    extra_values: dict = None,
    output_dir: str = "/output",
):
    """Write the standardized custom_metrics.json."""
    os.makedirs(output_dir, exist_ok=True)

    pass_rate = assertions_passed / max(assertions_total, 1)

    schema = {
        "build_success": "boolean",
        "assertions_total": "integer",
        "assertions_passed": "integer",
        "assertions_pass_rate": "float",
        "skill_name": "string",
        "plugin_name": "string",
        "source_type": "string",
        "difficulty": "string",
    }

    values = {
        "build_success": 1 if build_success else 0,
        "assertions_total": assertions_total,
        "assertions_passed": assertions_passed,
        "assertions_pass_rate": round(pass_rate, 3),
        "skill_name": skill_name,
        "plugin_name": plugin_name,
        "source_type": source_type,
        "difficulty": difficulty,
    }

    descriptions = {
        "build_success": "Whether the .NET solution builds after agent modifications",
        "assertions_total": "Total deterministic assertions evaluated",
        "assertions_passed": "Assertions that passed",
        "assertions_pass_rate": "Fraction of assertions passing (0.0-1.0)",
        "skill_name": "Name of the skill this task targets",
        "plugin_name": "Plugin containing the skill (dotnet or dotnet-msbuild)",
        "source_type": "Whether task was converted from eval.yaml or manually authored",
        "difficulty": "Task difficulty (easy/hard)",
    }

    # Merge extra values
    if extra_values:
        for key, val in extra_values.items():
            values[key] = val
            if isinstance(val, bool):
                schema[key] = "boolean"
            elif isinstance(val, int):
                schema[key] = "integer"
            elif isinstance(val, float):
                schema[key] = "float"
            else:
                schema[key] = "string"
            descriptions[key] = f"Custom metric: {key}"

    metrics = {
        "schema": schema,
        "values": values,
        "descriptions": descriptions,
    }

    with open(os.path.join(output_dir, "custom_metrics.json"), "w") as f:
        json.dump(metrics, f, indent=2)
    print(f"Wrote custom_metrics.json for {instance_id}")


def main():
    """CLI interface: write_eval.py <instance_id> <resolved> [--build-success] [--assertions-total N] [--assertions-passed N]"""
    if len(sys.argv) < 3:
        print(
            "Usage: write_eval.py <instance_id> <resolved:true|false> [options]",
            file=sys.stderr,
        )
        sys.exit(1)

    instance_id = sys.argv[1]
    resolved = sys.argv[2].lower() == "true"

    # Parse optional arguments
    build_success = True
    assertions_total = 0
    assertions_passed = 0
    skill_name = ""
    plugin_name = ""

    args = sys.argv[3:]
    i = 0
    while i < len(args):
        if args[i] == "--build-success":
            build_success = True
            i += 1
        elif args[i] == "--build-failure":
            build_success = False
            i += 1
        elif args[i] == "--assertions-total" and i + 1 < len(args):
            assertions_total = int(args[i + 1])
            i += 2
        elif args[i] == "--assertions-passed" and i + 1 < len(args):
            assertions_passed = int(args[i + 1])
            i += 2
        elif args[i] == "--skill" and i + 1 < len(args):
            skill_name = args[i + 1]
            i += 2
        elif args[i] == "--plugin" and i + 1 < len(args):
            plugin_name = args[i + 1]
            i += 2
        else:
            i += 1

    write_eval_json(instance_id, resolved)
    write_custom_metrics(
        instance_id, build_success, assertions_total, assertions_passed,
        skill_name=skill_name, plugin_name=plugin_name,
    )


if __name__ == "__main__":
    main()
