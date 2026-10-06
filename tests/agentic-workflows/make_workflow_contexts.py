"""Materialize flat body-expression inputs and complete offline runtime context."""

import argparse
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parent
HEAD = "a" * 40
MERGE = "b" * 40


def documents():
    for package in sorted(ROOT.iterdir()):
        fixtures = package / "fixtures"
        if not fixtures.is_dir():
            continue
        for case in sorted(fixtures.iterdir()):
            context_path = case / "context.json"
            context = json.loads(context_path.read_text(encoding="utf-8"))
            environment = context.setdefault("environment", {})
            expressions = {}
            if package.name == "build-failure-analysis":
                defaults = {
                    "GH_AW_BUILD_OUTCOME": "failure",
                    "GH_AW_BINLOG_LIST": "",
                    "GH_AW_BINLOG_DIR": "evidence",
                    "GH_AW_BINLOG_HOST_PATH": "https://dev.azure.com/fixture/project/_build/results?buildId=101",
                    "GH_AW_PR_NUMBER": "17",
                    "GH_AW_PR_HEAD_SHA": HEAD,
                    "GH_AW_PR_MERGE_SHA": MERGE,
                    "GH_AW_WORKSPACE": "repo",
                    "GH_AW_MISSING_LEGS": "",
                }
                for key, value in defaults.items():
                    environment.setdefault(key, value)
                environment.setdefault("GH_AW_BINLOG_PATH", environment["GH_AW_BINLOG_LIST"].split("\n")[0])
                context.setdefault("current_pr", {
                    "number": 17, "head": {"sha": HEAD}, "merge_commit_sha": MERGE,
                })
            elif package.name == "msbuild-quality-review":
                exclusions = context["excluded_paths"]
                environment["MSBUILD_QUALITY_REVIEW_EXCLUDED_PATHS"] = exclusions
                expressions = {
                    "github.event.pull_request.base.sha": context["pr"]["base_sha"],
                    "env.MSBUILD_QUALITY_REVIEW_EXCLUDED_PATHS": exclusions,
                }
            elif package.name == "test-failure-analysis":
                metadata = json.loads((case / "evidence" / "metadata.json").read_text(encoding="utf-8"))
                defaults = {
                    "GH_AW_EVIDENCE_SUMMARY_LOCATION": metadata["source"]["summary_location"],
                    "GH_AW_COMPLETENESS_REASONS": "; ".join(metadata["completeness"]["reasons"]) or "none",
                    "GH_AW_DURATION_REGRESSION_PERCENT": "25",
                    "GH_AW_DURATION_REGRESSION_MINIMUM_SECONDS": "30",
                    "GH_AW_DURATION_REGRESSION_MINIMUM_BASELINE_SAMPLES": "5",
                }
                for key, value in defaults.items():
                    environment.setdefault(key, value)
            yield context_path, json.dumps(context, indent=2, ensure_ascii=False) + "\n"
            yield case / "workflow-context.json", json.dumps(expressions, indent=2) + "\n"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    for path, content in documents():
        if args.check:
            actual = path.read_bytes().replace(b"\r\n", b"\n").decode("utf-8")
            if actual != content:
                raise ValueError(f"Workflow fixture context drift: {path.relative_to(ROOT)}")
        else:
            path.write_text(content, encoding="utf-8", newline="\n")
    print("Flat expression fixtures and runtime context are consistent.")


if __name__ == "__main__":
    main()
