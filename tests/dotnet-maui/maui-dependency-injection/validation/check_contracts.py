"""Replay advisory goldens and wrong answers; requires Python 3.10+ and PyYAML."""

import copy
import importlib.util
import json
from pathlib import Path
import sys

import yaml


sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[4]
SPEC = ROOT / "tests/dotnet-maui/maui-dependency-injection/eval.yaml"
loader = importlib.util.spec_from_file_location(
    "eval_quality", ROOT / "eng/eval-quality/check_eval_quality.py"
)
quality = importlib.util.module_from_spec(loader)
loader.loader.exec_module(quality)
raw = SPEC.read_text()
document = yaml.load(raw, Loader=quality.NoDuplicateKeys)
spec_name = str(SPEC.relative_to(ROOT))

quality.check_spec_shape(spec_name, document, raw)
quality.check_graders(spec_name, document)
quality.check_required_vally_inputs(spec_name, document)
quality.check_stimulus_names(spec_name, document)
quality.check_skill_constraints(spec_name, document)
quality.check_unquoted_rubric_code_tokens(spec_name, raw)
quality.check_fixtures(spec_name, document, set())
quality.check_references(spec_name, document, set())
assert not quality.errors, "\n".join(quality.errors)
eligible, dormancy, _, _ = quality.eval_evidence_counts(document)
assert eligible == 11 and dormancy == 2, (eligible, dormancy)
if "--export" not in sys.argv:
    print("PASS: scoped eval structure; 10 preference tasks, 1 no-op, 2 dormancy guards")

# Each answer represents a plausible defect, not random text or an empty result.
mutations = [
    "In CreateMauiApp use AddSingleton<IDataService, DataService>(), "
    "AddSingleton<DetailViewModel>() and AddSingleton<DetailPage>(). "
    "Assign BindingContext = vm; share the same detail edit state everywhere.",
    "The constructor cannot be injected: page registration is mandatory. "
    "An unregistered page throws even when its constructor services are registered.",
    "#if ANDROID register AndroidNotificationService; "
    "#elif IOS register AppleNotificationService; "
    "#elif WINDOWS register WindowsNotificationService; "
    "#endif. This covers all four declared targets.",
    "MAUI creates one IServiceScope per window. Root scopes aren't involved in "
    "window state. CreateScope and dispose it for each upload if needed, but "
    "a second window already has its own scope automatically.",
    "ContentTemplate pages bypass DI entirely. There is no constructor "
    "injection on that path; use a parameterless constructor instead of a route.",
    "Transient guarantees a constructor call on every tab return. "
    "DI is broken here; rebuild the container so refresh works.",
    "Transient resolves the context for each upload, even inside a singleton. "
    "Keep the current constructor and use the same context concurrently; "
    "dispose it only when the app closes.",
    "AddScoped is unusable in MAUI. Replace IEditorSession with a singleton "
    "and dispose it on application teardown; all windows should share it.",
    "All Shell templates ignore DI. Keep the manual new graph and change "
    "the fake's interface so ProductService automatically uses it.",
    "The container is unavailable until the first native window exists. "
    "Make another ServiceCollection and build another container in CreateWindow.",
    "You must change HomePage to transient. Singleton root pages are always "
    "incorrect, even when one tab intentionally keeps its state.",
    "Use a new window DI scope for each endpoint and retain it until the UI "
    "closes. Mobile navigation owns repository teardown.",
    "Use AddTransient<HomeViewModel>() to recreate BindingContext; "
    "there is no problem with the label path.",
]

stimuli = document["stimuli"]
assert len(stimuli) == len(mutations)
if "--export" in sys.argv:
    print(json.dumps(mutations))
    raise SystemExit(0)
for stimulus, mutation in zip(stimuli, mutations):
    trajectory = stimulus["golden_trajectory"]["inline"]
    quality.errors.clear()
    quality.check_trajectory_output_graders(spec_name, stimulus, trajectory, "golden")
    assert not quality.errors, "\n".join(quality.errors)
    broken = copy.deepcopy(trajectory)
    broken["steps"][-1]["message"] = mutation
    quality.check_trajectory_output_graders(spec_name, stimulus, broken, "mutation")
    assert any("fails its grader" in error for error in quality.errors), stimulus["name"]
    print(f"PASS: golden accepted / mutation rejected: {stimulus['name']}")

alternatives = {
    2: "Select AndroidNotificationService when OperatingSystem.IsAndroid(), "
    "AppleNotificationService when OperatingSystem.IsIOS() or IsMacCatalyst(), "
    "and WindowsNotificationService when OperatingSystem.IsWindows(); register "
    "the selected INotificationService as a shared service in startup. "
    "Explicitly reject unsupported targets.",
    3: 'The assertion "MAUI creates one IServiceScope per window" is false.\n',
    4: 'The claim "ContentTemplate pages bypass DI entirely" is incorrect.\n',
}
for index, text in alternatives.items():
    stimulus = stimuli[index]
    alternative = copy.deepcopy(stimulus["golden_trajectory"]["inline"])
    if index in (3, 4):
        text += alternative["steps"][-1]["message"]
    alternative["steps"][-1]["message"] = text
    quality.errors.clear()
    quality.check_trajectory_output_graders(spec_name, stimulus, alternative, "alternative")
    assert not quality.errors, "\n".join(quality.errors)
    print(f"PASS: valid alternative accepted: {stimulus['name']}")

print("PASS: all 13 golden responses and all 13 realistic mutations")

root_alternative = (
    "No, neither an empty constructor nor a replacement route is required. "
    "Typed ShellContent first tries GetService(typeof(DetailsPage)), then "
    "ActivatorUtilities.CreateInstance, which satisfies constructor parameters "
    "from DI even though the page itself is not registered."
)
alternative = copy.deepcopy(stimuli[4]["golden_trajectory"]["inline"])
alternative["steps"][-1]["message"] = root_alternative
quality.errors.clear()
quality.check_trajectory_output_graders(spec_name, stimuli[4], alternative, "alternative")
assert not quality.errors, "\n".join(quality.errors)
print("PASS: equivalent constructor-satisfaction wording accepted")
