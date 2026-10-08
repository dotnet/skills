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
    starts = [
        event["data"] for event in events if event["type"] == "tool.execution_start"
    ]
    completions = {
        event["data"]["toolCallId"]: event["data"]
        for event in events if event["type"] == "tool.execution_complete"
    }
    skills = [
        call["arguments"].get("skill")
        for call in starts if call["toolName"] == "skill"
    ]
    for name in ("grade-tests", "test-gap-analysis"):
        assert skills.count(name) == 1, f"Expected exactly one successful load of {name}: {skills}"
    assert set(skills) == {"grade-tests", "test-gap-analysis"}, f"Unexpected nested workflow: {skills}"
    for call in starts:
        assert call["toolName"] in READ_TOOLS, f"Unexpected execution or delegation: {call}"
        assert completions.get(call["toolCallId"], {}).get("success") is True, (
            f"Failed or incomplete tool call: {call}"
        )
    paths = [
        call["arguments"].get("path", "").replace("\\", "/")
        for call in starts if call["toolName"] == "view"
    ]
    assert any(path.endswith(
        "/test-gap-analysis/references/per-test-read-only.md"
    ) for path in paths), "The owned composition reference was not read"
    assert any(path.endswith(
        "/test-analysis-extensions/extensions/python.md"
    ) for path in paths), "Python assertion semantics were not loaded"
    gap_index = next(
        index for index, call in enumerate(starts)
        if call["toolName"] == "skill" and call["arguments"].get("skill") == "test-gap-analysis"
    )
    reference_index = next(
        index for index, call in enumerate(starts)
        if call["toolName"] == "view" and call["arguments"].get("path", "").replace("\\", "/")
        .endswith("/test-gap-analysis/references/per-test-read-only.md")
    )
    assert gap_index < reference_index, "Reference read did not follow the composition dispatch"
    output = next(
        event["data"]["content"]
        for event in reversed(events)
        if event["type"] == "assistant.message" and event["data"].get("content")
    )
    assert re.search(
        r"\|\s*Test\s*\|\s*Result\s*\|\s*Quality\s*\|\s*Notes\s*\|\s*How to improve\s*\|",
        output,
    ), "The grading report lost its required fields"
    assert re.search(
        r"test_standard_quote_calculates_cost[^\r\n]*Failed[^\r\n]*C\s*\(70.?79\)",
        output,
    ), "Incorrect individual readiness or quality result"
    assert re.search(r"Candidate survivor[\s\S]*unverified", output, re.I)
    assert re.search(r"cost[^\r\n]*10|10[^\r\n]*cost", output, re.I)
    assert not re.search(
        r"Pseudo.mutation[^\r\n]*N/A|^\s*(?:\*\*)?(?:Result:\s*)?(Strong|Mixed|Weak)\b",
        output, re.I | re.M,
    ), "Composition returned a fallback or a standalone suite verdict"
    assert not re.search(r"\b\d+\s*/\s*\d+\s*(mutations?|kill)|mutation score\s*[:=]\s*\d", output, re.I)
    return {"skills": skills, "tools": [call["toolName"] for call in starts], "output": output}


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
