"""Exercise the installed-plugin composition contract through the shipping CLI."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile


ROOT = Path(__file__).resolve().parents[3]
SUITE = Path(__file__).resolve().parent
PROMPT = (
    "Load the grade-tests skill by name and follow its workflow to grade only "
    "ShippingTests.test_standard_quote_calculates_cost in test_shipping.py for "
    "readiness, supporting A-F quality, and its smallest useful improvement. "
    "Production is shipping.py. This test claims the correct standard cost "
    "calculation for subtotal 50, not merely positivity. Leave files unchanged "
    "and do not run tests, build, import production, execute mutations, or "
    "invoke an agent. Include the evidence behind the finding."
)
READ_TOOLS = {"skill", "view", "glob", "grep", "rg"}


def digest_tree(root):
    return {
        str(path.relative_to(root)): hashlib.sha256(path.read_bytes()).hexdigest()
        for path in sorted(root.rglob("*")) if path.is_file()
    }


def verify_events(events):
    report_index, report = next(
        (index, event["data"]["content"])
        for index, event in reversed(list(enumerate(events)))
        if event["type"] == "assistant.message" and event["data"].get("content")
    )
    starts = [
        (index, event["data"]) for index, event in enumerate(events)
        if event["type"] == "tool.execution_start"
    ]
    completions = {
        event["data"]["toolCallId"]: (index, event["data"])
        for index, event in enumerate(events) if event["type"] == "tool.execution_complete"
    }
    skills = [
        call["arguments"].get("skill")
        for _, call in starts if call["toolName"] == "skill"
    ]
    for name in ("grade-tests", "test-gap-analysis"):
        assert skills.count(name) == 1, f"Expected exactly one successful load of {name}: {skills}"
    assert set(skills) == {"grade-tests", "test-gap-analysis"}, f"Unexpected nested workflow: {skills}"
    for index, call in starts:
        assert call["toolName"] in READ_TOOLS, f"Unexpected execution or delegation: {call}"
        completion_index, completion = completions.get(call["toolCallId"], (-1, {}))
        assert completion.get("success") is True, (
            f"Failed or incomplete tool call: {call}"
        )
        assert index < completion_index < report_index, (
            f"Tool must finish before the final report: {call}"
        )
    paths = [
        call["arguments"].get("path", "").replace("\\", "/")
        for _, call in starts if call["toolName"] == "view"
    ]
    assert any(path.endswith(
        "/test-gap-analysis/references/per-test-read-only.md"
    ) for path in paths), "The owned composition reference was not read"
    assert any(path.endswith(
        "/test-analysis-extensions/extensions/python.md"
    ) for path in paths), "Python assertion semantics were not loaded"
    grade_call = next(call for _, call in starts
                      if call["toolName"] == "skill" and call["arguments"].get("skill") == "grade-tests")
    gap_index, gap_call = next(
        (index, call) for index, call in starts
        if call["toolName"] == "skill" and call["arguments"].get("skill") == "test-gap-analysis")
    reference_index = next(
        index for index, call in starts if call["toolName"] == "view"
        and call["arguments"].get("path", "").replace("\\", "/")
        .endswith("/test-gap-analysis/references/per-test-read-only.md"))
    assert completions[grade_call["toolCallId"]][0] < gap_index, (
        "Gap analysis loaded before grading was loaded successfully"
    )
    assert completions[gap_call["toolCallId"]][0] < reference_index, (
        "Reference read did not follow successful composition dispatch"
    )
    output = report
    assert re.search(
        r"\|\s*Test\s*\|\s*Result\s*\|\s*Quality\s*\|\s*Notes\s*\|\s*How to improve\s*\|",
        output,
    ), "The grading report lost its required fields"
    assert re.search(
        r"test_standard_quote_calculates_cost[^\r\n]*Failed[^\r\n]*C\s*\(70.?79\)",
        output,
    ), "Incorrect individual readiness or quality result"
    assert re.search(r"Candidate survivor[\s\S]*unverified", output, re.I)
    target_rows = [
        line.split("|")[1:-1] for line in output.splitlines()
        if line.lstrip().startswith("|")
        and re.search(r"\btest_standard_quote_calculates_cost\b", line.split("|")[1])
    ]
    assert len(target_rows) == 1 and len(target_rows[0]) == 5, (
        "Expected exactly one complete target grading row"
    )
    action = target_rows[0][-1]
    assert re.search(r"cost[^\r\n]*\b10\b|\b10\b[^\r\n]*cost", action, re.I), (
        "The target How to improve cell must specify the concrete cost-10 assertion"
    )
    assert not re.search(
        r"Pseudo.mutation[^\r\n]*N/A|^\s*(?:\*\*)?(?:Result:\s*)?(Strong|Mixed|Weak)\b",
        output, re.I | re.M,
    ), "Composition returned a fallback or a standalone suite verdict"
    assert not re.search(r"\b\d+\s*/\s*\d+\s*(mutations?|kill)|mutation score\s*[:=]\s*\d", output, re.I)
    return {"skills": skills, "tools": [call["toolName"] for _, call in starts], "output": output}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cli", required=True, help="Shipping Copilot CLI executable")
    parser.add_argument("--model", required=True)
    parser.add_argument("--results-dir", required=True, type=Path)
    args = parser.parse_args()
    args.results_dir.mkdir(parents=True, exist_ok=True)
    version = subprocess.run(
        [args.cli, "--version"], check=True, capture_output=True, text=True, timeout=30,
    ).stdout.strip().splitlines()[0]
    environment = dict(os.environ)
    if not any(environment.get(key) for key in ("COPILOT_GITHUB_TOKEN", "GH_TOKEN", "GITHUB_TOKEN")):
        token = subprocess.run(
            ["gh", "auth", "token"], check=True, capture_output=True, text=True, timeout=30,
        ).stdout.strip()
        if not token:
            raise RuntimeError("GitHub CLI did not supply a token for the isolated run")
        environment["COPILOT_GITHUB_TOKEN"] = token
    with tempfile.TemporaryDirectory(prefix="composition-", dir=args.results_dir) as temporary:
        root = Path(temporary)
        workspace = root / "workspace"
        shutil.copytree(SUITE / "fixtures" / "focused-mutations", workspace)
        plugin = root / "dotnet-test"
        shutil.copytree(ROOT / "plugins" / "dotnet-test", plugin)
        original = digest_tree(workspace)
        original_plugin = digest_tree(plugin)
        environment["COPILOT_HOME"] = str(root / "copilot-home")
        command = [
            args.cli, "-C", str(workspace), "--plugin-dir", str(plugin),
            "--add-dir", str(plugin), "--no-auto-update", "--disable-builtin-mcps",
            "--no-ask-user", "--allow-all-tools", "--available-tools=skill,view,glob,rg",
            "--model", args.model, "--output-format", "json", "--stream", "off",
            "-p", PROMPT + (
                "\nHost-supplied language reference: read the matching bundled Python "
                "extension directly at " +
                str(plugin / "skills" / "test-analysis-extensions" / "extensions" / "python.md") +
                ". Do not invoke its reference-only loader; this is the actual shipped file."
            ),
        ]
        result = subprocess.run(
            command, env=environment, capture_output=True, text=True,
            encoding="utf-8", timeout=180,
        )
        (args.results_dir / "events.jsonl").write_text(result.stdout, encoding="utf-8")
        (args.results_dir / "stderr.txt").write_text(result.stderr, encoding="utf-8")
        assert result.returncode == 0, f"CLI failed ({result.returncode}): {result.stderr}"
        events = [json.loads(line) for line in result.stdout.splitlines() if line.strip()]
        evidence = verify_events(events)
        assert digest_tree(workspace) == original, "Analysis changed or executed fixture files"
        assert digest_tree(plugin) == original_plugin, "Analysis changed plugin files"
        evidence.update(cli_version=version, model=args.model, exit_code=result.returncode)
        (args.results_dir / "result.json").write_text(
            json.dumps(evidence, indent=2), encoding="utf-8",
        )
        print(f"{version}: read-only grading composition passed; dependencies and reference loaded.")


if __name__ == "__main__":
    main()
