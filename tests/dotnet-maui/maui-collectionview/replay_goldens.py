"""Replay deterministic MAUI references without agents, LLM judges, or native hosts.

Run from the repository root using a Python with PyYAML. Add --production to replay
all references with the pinned Vally oracle, with LLM graders removed. Requires
the repository's pinned Vally tooling and .NET 10 or later. Scratch workspaces stay inside each
suite and are removed after replay. MAUI objects here are real package types;
these checks do not test device layout, navigation, or OS lifecycle delivery.
"""
from pathlib import Path
import importlib.util
import json
import os
import re
import shutil
import subprocess
import sys

import yaml

NAMES = (
    "maui-app-lifecycle", "maui-safe-area", "maui-data-binding",
    "maui-collectionview", "maui-shell-navigation", "maui-theming",
)
# These are actual wrong recommendations, not an empty/unrelated response. A
# presence regex may accept one; report that honestly rather than calling it a
# semantic test. The executable fixtures cover the compile/state defects.
WRONG_ADVICE = {
    "maui-app-lifecycle": ("Save and restore state on background",
        "Save in Window.Stopped and restore only in Window.Resumed. Resumed always fires on first launch, so no cold-start initialization is needed."),
    "maui-safe-area": ("Keyboard avoidance with safe area for chat UI",
        'Use <ScrollView SafeAreaEdges="SoftInput"><Grid Spacing="10"><Entry /></Grid></ScrollView>; keyboard avoidance works directly on the scroller.'),
    "maui-data-binding": ("Set up compiled bindings with x:DataType on a page",
        'Set x:DataType="vm:SettingsViewModel"; this creates the BindingContext. Enable <MauiEnableXamlCompilation>true</MauiEnableXamlCompilation> to compile every binding.'),
    "maui-collectionview": ("ItemSizingStrategy placement for uniform items",
        '<LinearItemsLayout ItemSizingStrategy="MeasureFirstItem" /> works for rows of varying heights; measuring once is always safe.'),
    "maui-shell-navigation": ("Set up Shell navigation with tabs and flyout",
        'Use ContentTemplate for lazy pages, but add Products and another Products subsection before the Active and Archived tabs. The extra visible level is required.'),
    "maui-theming": ("Swap theme dictionaries without destroying app styles",
        'Keep a previous-theme reference: private ResourceDictionary? _activeTheme; public static void ApplyTheme(ResourceDictionary theme) { merged.Remove(_activeTheme); merged.Add(theme); _activeTheme = theme; }'),
}
WRONG_BOUNDARY = {
    "maui-app-lifecycle": "For browser visibility use new Window(new ContentPage()) and its Stopped event.",
    "maui-safe-area": '<ContentPage SafeAreaEdges="Container" /> fixes your HTML website.',
    "maui-data-binding": "For WPF set <MauiStrictXamlCompilation>true</MauiStrictXamlCompilation>.",
    "maui-collectionview": '<CollectionView ItemsSource="{Binding Buttons}" /> is necessary for four static buttons.',
    "maui-shell-navigation": 'For Blazor await Shell.Current.GoToAsync("products?id=1").',
    "maui-theming": "For Windows Terminal use Application.Current.UserAppTheme = AppTheme.Dark;",
}
ROOT = Path(__file__).resolve().parents[3]


def command(args, cwd):
    return subprocess.run(args, cwd=cwd, text=True, capture_output=True)


def production_replay(suite, spec):
    """Use the shipping parser/oracle with only deterministic graders selected."""
    path = suite / ".oracle-eval.yaml"
    workspace = suite / ".oracle-workspace"
    if path.exists() or workspace.exists():
        raise RuntimeError(f"Refusing to overwrite oracle scratch files in {suite}")
    deterministic = json.loads(json.dumps(spec))
    for stimulus in deterministic["stimuli"]:
        stimulus["graders"] = [g for g in stimulus["graders"] if g["type"] != "prompt"]
        stimulus.pop("rubric", None)
    try:
        path.write_text(yaml.safe_dump(deterministic, sort_keys=False))
        for stimulus in deterministic["stimuli"]:
            workspace.mkdir()
            scratch = workspace / ".scratch"
            scratch.mkdir()
            # Both explicit and internal oracle scratch stay within this suite.
            env = dict(os.environ, VALLY_TELEMETRY_OPTOUT="1",
                       TMPDIR=str(scratch), GIT_CEILING_DIRECTORIES=str(suite))
            initialized = subprocess.run(["git", "init", "--quiet"], cwd=workspace,
                                         env=env, capture_output=True, text=True)
            assert initialized.returncode == 0, initialized.stderr
            args = ["node", "eng/evaluation-tools/vally.mjs", "oracle",
                    "--eval-spec", str(path), "--stimulus", stimulus["name"],
                    "--workspace", str(workspace), "--output", "jsonl"]
            result = subprocess.run(args, cwd=ROOT, env=env, text=True, capture_output=True)
            records = [json.loads(line) for line in result.stdout.splitlines() if line.startswith("{")]
            assert result.returncode == 0 and len(records) == 1, result.stdout + result.stderr
            assert records[0]["status"] == "success" and records[0]["gradeResult"]["passed"], result.stdout
            shutil.rmtree(workspace)
    finally:
        path.unlink(missing_ok=True)
        if workspace.exists():
            shutil.rmtree(workspace)


def main():
    loader = importlib.util.spec_from_file_location(
        "quality_gate", ROOT / "eng/eval-quality/check_eval_quality.py")
    gate = importlib.util.module_from_spec(loader)
    loader.loader.exec_module(gate)
    totals = {"references": 0, "output_mutations": 0, "golden_workspaces": 0,
              "defect_rejections": 0, "preservation_rejections": 0,
              "routing_output_rejections": 0, "semantic_advice_not_proven": 0}
    for name in NAMES:
        suite = ROOT / "tests/dotnet-maui" / name
        spec = yaml.safe_load((suite / "eval.yaml").read_text())
        for stimulus in spec["stimuli"]:
            reference = stimulus["golden_trajectory"]["inline"]
            gate._validate_atif_trajectory(reference)
            response = reference["steps"][-1]["message"]
            for grader in stimulus["graders"]:
                if grader["type"] == "output-matches":
                    pattern = grader["config"]["pattern"]
                    assert gate.vally_regex_found(pattern, response), (name, stimulus["name"], pattern)
                    assert not gate.vally_regex_found(pattern, "Unrelated answer."), pattern
                    totals["output_mutations"] += 1
                elif grader["type"] == "output-not-matches":
                    pattern = grader["config"]["pattern"]
                    assert not gate.vally_regex_found(pattern, response), (name, stimulus["name"], pattern)
                    assert gate.vally_regex_found(pattern, WRONG_BOUNDARY[name]), name
                    totals["routing_output_rejections"] += 1
            gate.check_trajectory_claims(
                str(suite / "eval.yaml"), stimulus, reference,
                f"{suite / 'eval.yaml'}#{stimulus['name']}",
            )
            totals["references"] += 1
        title, wrong = WRONG_ADVICE[name]
        advice = next(s for s in spec["stimuli"] if s["name"] == title)
        matches = [g for g in advice["graders"] if g["type"] == "output-matches"]
        if all(gate.vally_regex_found(g["config"]["pattern"], wrong) for g in matches):
            totals["semantic_advice_not_proven"] += 1
            print(f"{name}: realistic incorrect advice passes presence regex; semantic judging remains pending")
        for stimulus in spec["stimuli"][-3:-1]:
            workspace = suite / ".replay-workspace"
            if workspace.exists():
                raise RuntimeError(f"Refusing to overwrite {workspace}")
            try:
                workspace.mkdir()
                initialized = command(["git", "init", "--quiet"], workspace)
                assert initialized.returncode == 0, initialized.stderr
                fixture = suite / stimulus["environment"]["files"][0]["src"]
                for source in fixture.iterdir():
                    if source.is_file():
                        shutil.copy2(source, workspace / source.name)
                if "golden_patch" in stimulus:
                    patch = (suite / stimulus["golden_patch"]["path"]).resolve()
                    checked = command(["git", "apply", "--check", str(patch)], workspace)
                    assert checked.returncode == 0, checked.stderr
                    applied = command(["git", "apply", str(patch)], workspace)
                    assert applied.returncode == 0, applied.stderr
                for grader in stimulus["graders"]:
                    if grader["type"] != "run-command":
                        continue
                    cfg = grader["config"]
                    result = command(["bash", "-c", cfg["command"]], workspace)
                    assert result.returncode == cfg.get("expected_exit_code", 0), result.stdout + result.stderr
                    if "stdout_matches" in cfg:
                        assert re.search(cfg["stdout_matches"], result.stdout), result.stdout
                totals["golden_workspaces"] += 1
                # Undo the actual repair: the production executable must reject it.
                if "golden_patch" in stimulus:
                    shutil.copy2(fixture / "Program.cs", workspace / "Program.cs")
                    for generated in ("obj", "bin"):
                        shutil.rmtree(workspace / generated, ignore_errors=True)
                    result = command(["dotnet", "run", "--project", "Fixture.csproj", "--verbosity", "quiet"], workspace)
                    assert result.returncode != 0, f"{name}: seeded defect escaped"
                    assert "PASS: behavior-contract" not in result.stdout
                    totals["defect_rejections"] += 1
                else:
                    # Corrupt every supplied file in turn: no representative-only check.
                    preservation = stimulus["graders"][0]["config"]["command"]
                    for filename in ("Program.cs", "Checks.cs", "Fixture.csproj",
                                     "Directory.Build.props", "Directory.Build.targets"):
                        target = workspace / filename
                        original = target.read_bytes()
                        try:
                            target.write_bytes(original + b"\n")
                            assert command(["bash", "-c", preservation], workspace).returncode != 0
                        finally:
                            target.write_bytes(original)
                        totals["preservation_rejections"] += 1
                    for filename, content in (
                        ("Unrequested.cs", "public class Unrequested {}\n"),
                        ("Unrequested.xaml", "<ContentPage />\n"),
                    ):
                        extra = workspace / "nested" / filename
                        extra.parent.mkdir(exist_ok=True)
                        extra.write_text(content)
                        assert command(["bash", "-c", preservation], workspace).returncode != 0
                        extra.unlink()
                        totals["preservation_rejections"] += 1
            finally:
                shutil.rmtree(workspace)
        if "--production" in sys.argv:
            production_replay(suite, spec)
        print(f"{name}: references, repaired/working execution and mutation rejection pass")
    assert not gate.errors, "\n".join(gate.errors)
    print(json.dumps(totals, sort_keys=True))


if __name__ == "__main__":
    main()
