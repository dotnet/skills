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
import xml.etree.ElementTree as ET

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


def api_probe():
    """Probe load-bearing snippets against the same real package as the fixtures."""
    binding_skill = ROOT / "plugins/dotnet-maui/skills/maui-data-binding/SKILL.md"
    binding_markup = binding_skill.read_text().split(
        "```xml\n<!-- ✅ Correct: x:DataType at the page root -->", 1)[1].split(
        "<!-- ❌ Wrong:", 1)[0]
    page = ET.fromstring('<Root xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml">'
                        + binding_markup + "</Root>")[0]
    assert len(page) == 1 and page[0].tag == "StackLayout", "page has multiple content roots"
    layout = page[0]
    page.remove(layout)
    page.extend(list(layout))
    assert len(page) > 1, "malformed sibling-content mutation was not reproduced"
    print("PASS: binding-markup single-root/mutation checks (not XAML compilation)")
    suite = ROOT / "tests/dotnet-maui/maui-collectionview"
    workspace = suite / ".api-probe-workspace"
    if workspace.exists():
        raise RuntimeError(f"Refusing to overwrite {workspace}")
    theme_skill = ROOT / "plugins/dotnet-maui/skills/maui-theming/SKILL.md"
    theme_manager = theme_skill.read_text().split(
        "```csharp\npublic sealed class ThemeManager\n", 1)[1].split(
        "\npublic partial class App", 1)[0]
    event_reference = ROOT / "plugins/dotnet-maui/skills/maui-theming/references/event-ownership.md"
    event_page = event_reference.read_text().split("```csharp\n", 1)[1].split("\n```", 1)[0]
    event_page = "\n".join(line for line in event_page.splitlines() if not line.startswith("using "))
    source = """using System.ComponentModel;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.PlatformConfiguration;
using Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific;
using Application = Microsoft.Maui.Controls.Application;
public static class Checks
{
    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    public static void Main()
    {
        Require(new CollectionView().ItemSizingStrategy == ItemSizingStrategy.MeasureAllItems,
            "MAUI sizing default changed");
        Require(typeof(LinearItemsLayout).GetProperty("ItemSizingStrategy") is null,
            "sizing property owner changed");
        Require(typeof(ContentPage).GetProperty("Content")!.PropertyType == typeof(View),
            "page content is not a single View");
        var parent = new Grid();
        var child = new Label();
        parent.Children.Add(child);
        var first = new object();
        var second = new object();
        parent.BindingContext = first;
        Require(ReferenceEquals(child.BindingContext, first), "context not inherited");
        parent.BindingContext = second;
        Require(ReferenceEquals(child.BindingContext, second), "context replacement not propagated");
        var converter = TypeDescriptor.GetConverter(typeof(SafeAreaEdges));
        var perEdge = (SafeAreaEdges)converter.ConvertFromInvariantString(
            "Container,Container,Container,SoftInput")!;
        Require(perEdge.Left == SafeAreaRegions.Container &&
            perEdge.Top == SafeAreaRegions.Container &&
            perEdge.Right == SafeAreaRegions.Container &&
            perEdge.Bottom == SafeAreaRegions.SoftInput, "valid per-edge syntax rejected");
        var axes = (SafeAreaEdges)converter.ConvertFromInvariantString("Container,SoftInput")!;
        Require(axes.Left == SafeAreaRegions.Container && axes.Right == SafeAreaRegions.Container &&
            axes.Top == SafeAreaRegions.SoftInput && axes.Bottom == SafeAreaRegions.SoftInput,
            "two-value form confused with region combination");
        Require(typeof(Image).GetProperty("SafeAreaEdges") is null,
            "unsupported Image property assumption changed");
        var legacyPage = new ContentPage();
        legacyPage.On<iOS>().SetUseSafeArea(true);
        Require(legacyPage.On<iOS>().UsingSafeArea(), "legacy C# safe-area setup failed");
        foreach (bool explicitWrapper in new[] { false, true })
        {
            var shell = new Shell();
            var item = new FlyoutItem { Route = "dashboard" };
            var content = new ShellContent {
                Route = "home", ContentTemplate = new DataTemplate(() => new ContentPage()) };
            if (explicitWrapper)
            {
                var tab = new Tab();
                tab.Items.Add(content);
                item.Items.Add(tab);
            }
            else
                item.Items.Add(content);
            shell.Items.Add(item);
            Require(item.Items.Count == 1 && item.Items[0].Items.Count == 1,
                "explicit/implicit wrappers differ in hierarchy");
            Require(!((IShellItemController)item).ShowTabs,
                "single-page wrapper unexpectedly forces a tab bar");
            item.FlyoutDisplayOptions = FlyoutDisplayOptions.AsMultipleItems;
            Require(!((IShellItemController)item).ShowTabs,
                "flyout display options confused with tab-bar visibility");
        }
        var publisher = new Application();
        var firstPage = new ProbeThemeAwarePage(publisher);
        var secondPage = new ProbeThemeAwarePage(publisher);
        firstPage.Appear(); firstPage.Appear(); secondPage.Appear();
        publisher.UserAppTheme = AppTheme.Dark;
        Require(firstPage.Callbacks == 1 && secondPage.Callbacks == 1,
            "appearance registered duplicate callbacks");
        firstPage.Disappear();
        publisher.UserAppTheme = AppTheme.Light;
        Require(firstPage.Callbacks == 1 && secondPage.Callbacks == 2,
            "page cleanup affected another subscriber");
        firstPage.Appear();
        publisher.UserAppTheme = AppTheme.Dark;
        Require(firstPage.Callbacks == 2 && secondPage.Callbacks == 3,
            "reappearing page did not resume observation");
        firstPage.Disappear(); secondPage.Disappear();
        var resources = new ResourceDictionary();
        var styles = new ResourceDictionary { ["Brand"] = "retained" };
        var light = new ResourceDictionary { ["Text"] = "black" };
        var dark = new ResourceDictionary { ["Text"] = "white" };
        resources.MergedDictionaries.Add(styles);
        var manager = new ThemeManager(resources);
        manager.ApplyTheme(light);
        manager.ApplyTheme(dark);
        manager.ApplyTheme(light);
        Require(resources.MergedDictionaries.Count == 2 &&
            resources.MergedDictionaries.Contains(styles) &&
            resources.MergedDictionaries.Contains(light) &&
            !resources.MergedDictionaries.Contains(dark), "theme ownership/preservation failed");
        Console.WriteLine("PASS: package-api-probe (no XAML/native/device execution)");
    }
}
public sealed class ThemeManager
""" + theme_manager + "\n" + event_page + """
public sealed class ProbeThemeAwarePage(Application publisher) : ThemeAwarePage(publisher)
{
    public int Callbacks { get; private set; }
    public void Appear() => base.OnAppearing();
    public void Disappear() => base.OnDisappearing();
    protected override void OnThemeChanged(object? sender, AppThemeChangedEventArgs e)
        => Callbacks++;
}
"""
    try:
        workspace.mkdir()
        for filename in ("Fixture.csproj", "Directory.Build.props", "Directory.Build.targets"):
            shutil.copy2(suite / "fixtures/working" / filename, workspace / filename)
        (workspace / "Program.cs").write_text(source)
        result = command(["dotnet", "run", "--project", "Fixture.csproj", "--verbosity", "quiet"], workspace)
        assert result.returncode == 0, result.stdout + result.stderr
        assert "PASS: package-api-probe" in result.stdout, result.stdout
        print(result.stdout.strip())
    finally:
        shutil.rmtree(workspace)


def production_replay(suite, spec, expected_pass=True):
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
            assert len(records) == 1, result.stdout + result.stderr
            assert records[0]["gradeResult"]["passed"] is expected_pass, result.stdout
            if expected_pass:
                assert result.returncode == 0 and records[0]["status"] == "success", result.stdout
            else:
                assert any(g.get("graderType") == "output-matches" and not g["passed"]
                           for g in records[0]["gradeResult"]["details"]), result.stdout
            shutil.rmtree(workspace)
    finally:
        path.unlink(missing_ok=True)
        if workspace.exists():
            shutil.rmtree(workspace)


def wording_regressions(production=False):
    """Accept equivalent domain evidence without accepting relative/bare routes."""
    cases = (
        ("maui-app-lifecycle", "Two windows with different drafts",
         "Each editor window owns its document and subscriptions.",
         "Use one global current document for every editor."),
        ("maui-app-lifecycle", "Two windows with different drafts",
         "Use one owner object per `Window`. The owner contains that window’s document/session.",
         "Every editor changes one shared document and unsubscribes all listeners on close."),
        ("maui-app-lifecycle", "Interruption is not background",
         "Distinguish temporary interruption/inactivity from session-ending backgrounding or termination.",
         "Discard the timer session whenever the screen loses focus."),
        ("maui-app-lifecycle", "Long upload on app switch",
         "OnStopped handlers aren't guaranteed to complete. Persist pending intent and retry.",
         "Stopped grants a guaranteed minute of background execution."),
        ("maui-safe-area", "Landscape inset change",
         "Use SafeAreaEdges so the OS computes insets per device and orientation.",
         "Hardcode Margin=44 on every rotation."),
        ("maui-safe-area", "ScrollView keyboard wrapper",
         "Wrap the ScrollView in a Grid; that Grid owns the keyboard inset.",
         "Set SoftInput directly on the ScrollView and nothing else."),
        ("maui-safe-area", "ScrollView keyboard wrapper",
         "The page/window root owns the keyboard inset; keep the ScrollView in a bounded Grid row.",
         "Only the ScrollView consumes SoftInput; leave the button outside inset handling."),
        ("maui-shell-navigation", "Stable routes for deep linking into tabs",
         'await Shell.Current.GoToAsync("//tasks/archived/archivedlist");',
         'await Shell.Current.GoToAsync("tasks/archived/archivedlist");'),
        ("maui-shell-navigation", "Repair supplied executable contract",
         'Absolute URI: //products/status/archived',
         "Absolute path: products/status/archived"),
        ("maui-collectionview", "Incremental fetch overlap",
         "if (_isLoading || !_hasMore) return; _isLoading = true; "
         "try { await FetchAsync(); } finally { _isLoading = false; }",
         "private bool _isLoading; await FetchAsync();"),
        ("maui-theming", "Swap theme dictionaries without destroying app styles",
         "if (_activeTheme is not null) dictionaries.Remove(_activeTheme); "
         "_activeTheme = new DarkTheme(); dictionaries.Add(_activeTheme);",
         "dictionaries.Clear(); dictionaries.Add(new DarkTheme());"),
        ("maui-theming", "Swap theme dictionaries without destroying app styles",
         "foreach (var d in dictionaries.Where(d => d is LightTheme || d is DarkTheme).ToList()) "
         "dictionaries.Remove(d); dictionaries.Add(new DarkTheme());",
         "dictionaries.Add(new DarkTheme());"),
        ("maui-theming", "Explicit theme event ownership",
         "_observedApplication.RequestedThemeChanged -= OnRequestedThemeChanged;",
         "_observedApplication.RequestedThemeChanged += OnRequestedThemeChanged;"),
    )
    gate_spec = importlib.util.spec_from_file_location(
        "quality_gate", ROOT / "eng/eval-quality/check_eval_quality.py")
    gate = importlib.util.module_from_spec(gate_spec)
    gate_spec.loader.exec_module(gate)
    for name, title, valid, invalid in cases:
        suite = ROOT / "tests/dotnet-maui" / name
        original = yaml.safe_load((suite / "eval.yaml").read_text())
        stimulus = next(s for s in original["stimuli"] if s["name"] == title)
        matches = [g for g in stimulus["graders"] if g["type"] == "output-matches"]
        assert all(gate.vally_regex_found(g["config"]["pattern"], valid) for g in matches), title
        assert any(not gate.vally_regex_found(g["config"]["pattern"], invalid) for g in matches), title
        if name == "maui-shell-navigation":
            wrong = "Use // for absolute navigation" if title.startswith("Stable") else "//tasks/status/archived"
            assert any(not gate.vally_regex_found(g["config"]["pattern"], wrong) for g in matches), title
        if production:
            for response, expected in ((valid, True), (invalid, False)):
                spec = json.loads(json.dumps(original))
                spec["stimuli"] = [next(s for s in spec["stimuli"] if s["name"] == title)]
                spec["stimuli"][0]["golden_trajectory"]["inline"]["steps"][-1]["message"] = response
                production_replay(suite, spec, expected_pass=expected)
    print(f"PASS: {len(cases)} equivalent-wording acceptances and {len(cases)} contract mutations" +
          (" through shipping Vally" if production else ""))


def main():
    loader = importlib.util.spec_from_file_location(
        "quality_gate", ROOT / "eng/eval-quality/check_eval_quality.py")
    gate = importlib.util.module_from_spec(loader)
    loader.loader.exec_module(gate)
    totals = {"references": 0, "output_mutations": 0, "golden_workspaces": 0,
              "defect_rejections": 0, "preservation_rejections": 0,
              "routing_output_rejections": 0, "semantic_advice_not_proven": 0,
              "setup_rejections": 0}
    for name in NAMES:
        suite = ROOT / "tests/dotnet-maui" / name
        spec = yaml.safe_load((suite / "eval.yaml").read_text())
        for stimulus in spec["stimuli"]:
            reference = stimulus["golden_trajectory"]["inline"]
            gate._validate_atif_trajectory(reference)
            response = reference["steps"][-1]["message"]
            for grader in stimulus["graders"]:
                if grader["type"] == "prompt":
                    assert "golden_patch" not in grader.get("config", {}).get("evidence", []), (
                        name, stimulus["name"], "Vally eval does not resolve golden_patch evidence")
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
                for source in fixture.rglob("*"):
                    if source.is_file():
                        target = workspace / source.relative_to(fixture)
                        target.parent.mkdir(parents=True, exist_ok=True)
                        shutil.copy2(source, target)
                for setup in stimulus["environment"].get("commands", []):
                    result = command(["bash", "-c", setup], workspace)
                    assert result.returncode == 0, result.stdout + result.stderr
                # A prior arm's repair must fail setup rather than look like a
                # no-op success in the next arm.
                if stimulus["environment"].get("commands"):
                    target = workspace / "Program.cs"
                    original = target.read_bytes()
                    try:
                        target.write_bytes(original + b"\n")
                        assert any(command(["bash", "-c", setup], workspace).returncode != 0
                                   for setup in stimulus["environment"]["commands"])
                    finally:
                        target.write_bytes(original)
                    totals["setup_rejections"] += 1
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
                    for source in fixture.rglob("*"):
                        if not source.is_file():
                            continue
                        target = workspace / source.relative_to(fixture)
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
                        ("Unrequested.resx", "<root />\n"),
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
    api_probe()
    wording_regressions("--production" in sys.argv)
    print(json.dumps(totals, sort_keys=True))


if __name__ == "__main__":
    main()
