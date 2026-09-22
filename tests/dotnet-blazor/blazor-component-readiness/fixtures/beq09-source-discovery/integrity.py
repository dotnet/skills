"""Check this fixed source pair and its declared graders without running an assessor."""

import argparse
import copy
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET

import yaml


HERE = Path(__file__).resolve().parent
REPOSITORY = HERE.parents[4]
EVAL = HERE.parents[1] / "eval.yaml"
FILES = ("SelectionToggle.csproj", "SelectionStateBase.cs",
         "SelectionToggle.razor", "SelectionToggle.razor.cs")
CASES = ("case-a", "case-b")
NAMES = ("Assess a local selection component", "Assess a separate local selection component")
ANCHORS = (
    (("SelectionToggle.razor.cs", 15, 15), ("SelectionStateBase.cs", 7, 8),
     ("SelectionToggle.razor", 2, 2)),
    (("SelectionToggle.razor.cs", 15, 15), ("SelectionStateBase.cs", 16, 16),
     ("SelectionStateBase.cs", 7, 14), ("SelectionStateBase.cs", 20, 20),
     ("SelectionToggle.razor.cs", 18, 18), ("SelectionToggle.razor", 2, 2),
     ("SelectionToggle.razor", 5, 5)),
)


def require(condition, message):
    if not condition:
        raise ValueError(message)


def text(path):
    return path.read_bytes().decode("utf-8").replace("\r\n", "\n")


def digest(path):
    return hashlib.sha256(text(path).encode("utf-8")).hexdigest()


def save(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def command(argv, cwd, log, environment):
    result = subprocess.run(argv, cwd=cwd, env=environment, capture_output=True,
                            text=True, encoding="utf-8", errors="strict", timeout=180)
    save(log, {"argv": argv, "cwd": str(cwd), "exit_code": result.returncode,
               "stdout": result.stdout, "stderr": result.stderr})
    return result


def check_fixture_and_spec(spec):
    require(len(spec["stimuli"]) == 38, "expected the existing 36 stimuli plus two source-discovery cases")
    require(spec["defaults"] == {"runs": 1, "timeout": "20m"}, "defaults changed")
    require(spec["scoring"] == {"threshold": 1.0}, "threshold changed")
    require(len({s["name"] for s in spec["stimuli"]}) == 38, "duplicate stimulus name")
    selected = spec["stimuli"][-2:]
    require(tuple(s["name"] for s in selected) == NAMES, "new stimulus identity changed")
    require(selected[0]["prompt"] == selected[1]["prompt"], "prompts must be identical")
    require(set(p.relative_to(HERE).as_posix() for p in HERE.rglob("*") if p.is_file()) ==
            {"criterion.json", "integrity.py"} | {f"{c}/{f}" for c in CASES for f in FILES},
            "unexpected repository fixture file")
    require(not any(p.is_symlink() for p in HERE.rglob("*")), "linked fixture input")
    for name in FILES:
        a, b = text(HERE / CASES[0] / name), text(HERE / CASES[1] / name)
        if name == "SelectionToggle.razor.cs":
            require(a.count("            Value = nextValue;") == 1, "defect anchor changed")
            require(a.replace("            Value = nextValue;", "            CurrentValue = nextValue;") == b,
                    "the pair differs beyond the one intended assignment target")
        else:
            require(a == b, "a shared source/project file differs")
    base = text(HERE / CASES[0] / "SelectionStateBase.cs")
    require(re.findall(r"\[Parameter\]\s+public ([\w<>]+) (\w+) \{ get; set; \}", base) ==
            [("bool", "Value"), ("bool", "AllowChange"), ("EventCallback<bool>", "ValueChanged")],
            "public parameter declarations changed")
    require(base.count("[Parameter]") == 3 and not re.search(r"\b(required|init)\b", base),
            "unexpected parameter declaration syntax")
    criterion = json.loads(text(HERE / "criterion.json"))
    require(criterion["requirement_id"] == "BEQ-09" and criterion["requirement"] ==
            "Use [Parameter] { get; set; } auto-properties; no required/init (BL0007) and no internal parameter mutation.",
            "shared normative criterion changed")
    require(set(criterion["status_definitions"]) ==
            {"verified", "gap", "owner evidence required", "not tested", "not applicable"},
            "shared status definitions are incomplete")
    for forbidden in (*CASES, "nextValue", *FILES):
        require(forbidden not in selected[0]["prompt"] and forbidden not in text(HERE / "criterion.json"),
                "case-specific locator or answer leaked into the prompt/criterion")
    for i, stimulus in enumerate(selected):
        case = CASES[i]
        staging = [{"src": f"fixtures/beq09-source-discovery/{case}/{f}",
                    "dest": f"fixture/component/{f}"} for f in FILES]
        staging.append({"src": "fixtures/beq09-source-discovery/criterion.json",
                        "dest": "fixture/criterion.json"})
        require(stimulus["environment"] == {"files": staging}, "unexpected staging or setup command")
        require([g["type"] for g in stimulus["graders"]] == ["file-exists", "program"],
                "unexpected grader type")
        require(stimulus["graders"][0]["config"] == {"path": "out/beq09-decision.json"},
                "wrong artifact path")
        config = stimulus["graders"][1]["config"]
        require(config["program"] == "python" and len(config["args"]) == 4 and
                config["args"][:2] == ["-I", "-c"], "unexpected program invocation")
        expected = json.loads(config["args"][3])
        require((expected["status"], expected["ownership"]) ==
                (("gap", "public-parameter") if i == 0 else ("verified", "internal-state")),
                "case label/ownership binding differs")
        require(expected["files"] == {f: digest(HERE / case / f) for f in FILES},
                "grader source hashes differ")
        require(expected["criterion_sha256"] == digest(HERE / "criterion.json"), "criterion hash differs")
        require(tuple((a["path"], a["start_line"], a["end_line"]) for a in expected["anchors"]) == ANCHORS[i],
                "case locator binding differs")
        for anchor in expected["anchors"]:
            lines = text(HERE / case / anchor["path"]).splitlines()
            require(anchor["text"] == "\n".join(lines[anchor["start_line"] - 1:anchor["end_line"]]),
                    "case anchor text differs")
        project = ET.fromstring(text(HERE / case / "SelectionToggle.csproj"))
        require(project.attrib == {"Sdk": "Microsoft.NET.Sdk.Razor"} and
                [node.tag for node in project] == ["PropertyGroup", "ItemGroup"] and
                [(p.tag, p.text, p.attrib) for p in project.find("PropertyGroup")] ==
                [("TargetFramework", "net11.0", {}), ("ImplicitUsings", "enable", {}),
                 ("Nullable", "enable", {}), ("RootNamespace", "SourceDiscovery", {})] and
                [(p.tag, p.attrib) for p in project.find("ItemGroup")] ==
                [("FrameworkReference", {"Include": "Microsoft.AspNetCore.App"})],
                "fixture project is not the inspected package-free Razor project")
    require(selected[0]["graders"][1]["config"]["args"][2] ==
            selected[1]["graders"][1]["config"]["args"][2], "grader code differs between cases")
    return selected


def compile_case(case, scratch, environment):
    dotnet = shutil.which("dotnet")
    require(dotnet is not None, "installed dotnet is required; do not install or use stubs")
    output = scratch / "build" / case
    output.mkdir(parents=True)
    project = HERE / case / "SelectionToggle.csproj"
    offline = (REPOSITORY / "plugins" / "dotnet-blazor" / "skills" / "blazor-component-readiness" /
               "scripts" / "validator" / "restore-offline.config")
    common = [dotnet, "msbuild", str(project), "-noAutoResponse", "-verbosity:minimal",
              "-property:Configuration=Release", "-property:ImportDirectoryBuildProps=false",
              "-property:ImportDirectoryBuildTargets=false", "-property:ImportDirectoryPackagesProps=false",
              "-property:NuGetAudit=false", f"-property:RestoreConfigFile={offline}",
              f"-property:RestorePackagesPath={scratch / 'packages'}",
              f"-property:BaseOutputPath={output / 'bin'}{os.sep}",
              f"-property:BaseIntermediateOutputPath={output / 'obj'}{os.sep}",
              "-property:EmitCompilerGeneratedFiles=true",
              f"-property:CompilerGeneratedFilesOutputPath={output / 'generated'}"]
    inventory = command(common + ["-target:ResolveRazorComponentInputs",
                                  "-getItem:Compile,RazorComponent,PackageReference,ProjectReference",
                                  "-getProperty:TargetFramework,NETCoreSdkVersion"],
                        REPOSITORY, output / "01-inventory.json", environment)
    require(inventory.returncode == 0, f"{case}: prebuild inventory failed")
    items = json.loads(inventory.stdout)
    require(items["Properties"]["TargetFramework"] == "net11.0", "unexpected target framework")
    require({Path(p["FullPath"]).resolve() for p in items["Items"]["Compile"]} ==
            {HERE / case / "SelectionStateBase.cs", HERE / case / "SelectionToggle.razor.cs"},
            "Compile includes anything outside the original fixture")
    require({Path(p["FullPath"]).resolve() for p in items["Items"]["RazorComponent"]} ==
            {HERE / case / "SelectionToggle.razor"}, "Razor source closure differs")
    require(not items["Items"]["PackageReference"] and not items["Items"]["ProjectReference"],
            "fixture acquired a package/project dependency")
    build = command(common + ["-target:Build"], REPOSITORY, output / "02-build.json", environment)
    if build.returncode != 0:
        require("NETSDK1004" in build.stdout + build.stderr, f"{case}: build failed for a non-assets reason")
        restore = command(common + ["-target:Restore"], REPOSITORY, output / "03-offline-restore.json", environment)
        require(restore.returncode == 0, f"{case}: offline restore failed; no installs/network fallback")
        build = command(common + ["-target:Build"], REPOSITORY, output / "04-build.json", environment)
    require(build.returncode == 0, f"{case}: compilation failed")
    require(not re.search(r"\bwarning (?:BL|RZ|CS)\d+", build.stdout + build.stderr),
            f"{case}: compiler/analyzer warnings require review")
    references = command(common + ["-target:ResolveReferences", "-getItem:ResolvedFrameworkReference"],
                         REPOSITORY, output / "05-references.json", environment)
    require(references.returncode == 0, f"{case}: framework reference inventory failed")
    packs = json.loads(references.stdout)["Items"]["ResolvedFrameworkReference"]
    require({p["Identity"] for p in packs} == {"Microsoft.NETCore.App", "Microsoft.AspNetCore.App"},
            "unexpected resolved frameworks")
    require(all(Path(p["TargetingPackPath"]).is_dir() for p in packs), "reference pack is missing")
    generated = list((output / "generated").rglob("*SelectionToggle_razor.g.cs"))
    require(len(generated) == 1, f"{case}: missing or ambiguous generated component")
    code = re.sub(r"(?m)^[ \t\ufeff]*#(?:line\b|nullable\b|pragma (?:checksum|warning)\b)[^\n]*",
                  "", text(generated[0]))
    require(re.search(r"partial class SelectionToggle\s*:\s*SelectionStateBase\s*\{", code) is not None,
            "generated component lost its owned base")
    require(re.search(r'"onclick"\s*,\s*(?:global::)?Microsoft\.AspNetCore\.Components\.EventCallback\.Factory'
                      r'\.Create<(?:global::)?Microsoft\.AspNetCore\.Components\.Web\.MouseEventArgs>'
                      r'\s*\(\s*this\s*,\s*HandleToggleAsync\s*\)', code) is not None,
            "Razor did not generate the typed onclick callback to the inspected handler")
    require('"@onclick"' not in code, "Razor emitted a literal event attribute instead of a binding")
    return {"sdk": items["Properties"]["NETCoreSdkVersion"], "frameworks": packs,
            "generated_source": str(generated[0]), "generated_sha256": digest(generated[0]),
            "assembly_executed": False}


def positive_worksheets():
    return [
        {"requirement_id": "BEQ-09", "status": "gap", "ownership": "public-parameter",
         "citations": [
             {"path": "SelectionToggle.razor.cs", "start_line": 14, "end_line": 16,
              "fact": "The conditional handler assigns nextValue through the inherited Value property."},
             {"path": "SelectionStateBase.cs", "start_line": 7, "end_line": 8,
              "fact": "Value is a public Parameter auto-property, not the component's state field."},
             {"path": "SelectionToggle.razor", "start_line": 1, "end_line": 3,
              "fact": "The rendered partial component inherits the owned SelectionStateBase declaration."}],
         "explanation": "The owned handler writes a public parameter setter. Legal auto-properties and the "
                        "later paired callback do not cancel that conflict. This is a source finding only."},
        {"requirement_id": "BEQ-09", "status": "verified", "ownership": "internal-state",
         "citations": [
             {"path": "SelectionToggle.razor.cs", "start_line": 12, "end_line": 18,
              "fact": "The handler updates CurrentValue and invokes, rather than assigns, ValueChanged."},
             {"path": "SelectionStateBase.cs", "start_line": 7, "end_line": 21,
              "fact": "All three public Parameters are get/set auto-properties with no required/init. "
                      "CurrentValue is protected state; OnParametersSet reads Value into that state."},
             {"path": "SelectionToggle.razor", "start_line": 2, "end_line": 6,
              "fact": "The component inherits that base and renders state with a connected event handler, "
                      "without two-way writes to a public parameter."}],
         "explanation": "Across the complete owned base, partial handler and Razor surface, public parameters "
                        "are ordinary auto-properties and component-owned writes target internal state. "
                        "The whole source-level row is satisfied; no runtime behavior is claimed."},
    ]


def check_grader_result(result, outcome):
    fields = {"name", "kind", "passed", "score", "evidence", "status"}
    require(isinstance(result, dict) and fields <= result.keys(), "incomplete GraderResult object")
    require(result["name"] == "beq09-source-discovery" and result["kind"] == "code",
            "unexpected grader identity or kind")
    require(result["status"] == ("error" if outcome == "error" else "success") and
            result["passed"] is (outcome == "pass") and type(result["score"]) in (int, float) and
            result["score"] == (1 if outcome == "pass" else 0), "incorrect GraderResult outcome")
    prefix = {"pass": "VALID BEQ-09 source-bound worksheet", "fail": "FAIL assessor output:",
              "error": "INVALID fixture/grader:"}[outcome]
    require(isinstance(result["evidence"], str) and result["evidence"].startswith(prefix),
            "missing grader evidence or incorrect failure category")


def grade_controls(stimuli, scratch, environment):
    worksheets = positive_worksheets()
    results = []
    for index, stimulus in enumerate(stimuli):
        def grade(label, answer, outcome="fail", raw=None, tamper=None, config=None):
            workspace = scratch / "controls" / CASES[index] / label
            workspace.mkdir(parents=True)
            for item in stimulus["environment"]["files"]:
                destination = workspace / item["dest"]
                destination.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(EVAL.parent / item["src"], destination)
            output = workspace / stimulus["graders"][0]["config"]["path"]
            if raw is not None:
                output.parent.mkdir(parents=True)
                output.write_bytes(raw)
            elif answer is not None:
                save(output, answer)
            if tamper is not None:
                tamper(workspace)
            declared = config or stimulus["graders"][1]["config"]
            executable = shutil.which(declared["program"])
            require(executable is not None, "declared Python grader prerequisite is missing")
            run = command([executable, *declared["args"]], workspace,
                          workspace.parent / f"{label}.grader.json", environment)
            require(run.returncode == 0, f"{CASES[index]}/{label}: grader process failed")
            result = json.loads(run.stdout)
            check_grader_result(result, outcome)
            require(output.is_file() == (answer is not None or raw is not None), "file-exists contract differs")
            results.append({"case": CASES[index], "control": label, "program_exit": run.returncode,
                            "file_exists": output.is_file(), "classification": outcome,
                            "grader_result": result})

        positive = worksheets[index]
        grade("valid", positive, "pass")
        grade("swapped-worksheet", worksheets[1 - index])
        grade("missing-output", None)
        for field in positive:
            answer = copy.deepcopy(positive)
            del answer[field]
            grade("missing-" + field, answer)
            answer[field] = None
            grade("null-" + field, answer)
        for label, field, value in [
            ("wrong-requirement", "requirement_id", "BEQ-08"),
            ("wrong-status", "status", "verified" if index == 0 else "gap"),
            ("unknown-status", "status", "passed"),
            ("wrong-ownership", "ownership", "internal-state" if index == 0 else "public-parameter"),
            ("unresolved-ownership", "ownership", "unresolved"),
            ("unknown-ownership", "ownership", "protected"),
            ("empty-citations", "citations", []),
            ("blank-explanation", "explanation", " \t"),
            ("extra-field", "extra", True),
        ]:
            answer = copy.deepcopy(positive)
            answer[field] = value
            grade(label, answer)
        for status in ("owner evidence required", "not tested", "not applicable"):
            answer = copy.deepcopy(positive)
            answer.update(status=status, ownership="unresolved")
            grade(status.replace(" ", "-"), answer)
        for label, changes in [
            ("wrong-file", {"path": "SelectionToggle.csproj", "start_line": 1, "end_line": 1}),
            ("unrelated-lines", {"start_line": 1, "end_line": 1}),
            ("unknown-file", {"path": "Outside.cs"}),
            ("sibling-path", {"path": "case-b/SelectionToggle.razor.cs"}),
            ("traversal", {"path": "../SelectionToggle.razor.cs"}),
            ("absolute-path", {"path": "/SelectionToggle.razor.cs"}),
            ("drive-path", {"path": "C:\\SelectionToggle.razor.cs"}),
            ("zero-line", {"start_line": 0}),
            ("reversed-range", {"start_line": 16, "end_line": 15}),
            ("beyond-file", {"end_line": 1000}),
            ("boolean-line", {"start_line": True}),
            ("fractional-line", {"start_line": 14.0}),
            ("string-line", {"start_line": "14"}),
            ("blank-fact", {"fact": "\n\t"}),
            ("extra-citation-field", {"extra": "ignored?"}),
        ]:
            answer = copy.deepcopy(positive)
            answer["citations"][0].update(changes)
            grade(label, answer)
        answer = copy.deepcopy(positive)
        answer["citations"] = [answer["citations"][0]]
        grade("missing-declaration-connection", answer)
        for label, raw in [
            ("invalid-json", b"{"), ("invalid-utf8", b"\xff"),
            ("root-array", b"[]"), ("root-null", b"null"),
            ("extra-object", json.dumps(positive).encode("utf-8") + b" {}"),
            ("duplicate-key", json.dumps(positive).encode("utf-8")[:-1] + b', "status": "gap"}'),
            ("non-json-number", json.dumps(positive).encode("utf-8")[:-1] + b', "extra": NaN}'),
        ]:
            grade(label, None, raw=raw)
        for name in FILES:
            def alter(workspace, name=name):
                path = workspace / "fixture" / "component" / name
                path.write_bytes(path.read_bytes() + b"\n")
            grade("tamper-" + name, positive, "error", tamper=alter)
        def alter_criterion(workspace):
            path = workspace / "fixture" / "criterion.json"
            path.write_bytes(path.read_bytes() + b"\n")
        grade("tamper-criterion", positive, "error", tamper=alter_criterion)
        grade("missing-source", positive, "error",
              tamper=lambda w: (w / "fixture" / "component" / FILES[1]).unlink())
        grade("extra-source", positive, "error",
              tamper=lambda w: (w / "fixture" / "component" / "Unexpected.cs").write_text("// extra\n"))
        grade("swapped-grader-config", positive, "error", config=stimuli[1 - index]["graders"][1]["config"])
        config = copy.deepcopy(stimulus["graders"][1]["config"])
        expected = json.loads(config["args"][3])
        expected["anchors"][0]["text"] = "not the declared source"
        config["args"][3] = json.dumps(expected)
        grade("invalid-grader-anchor", positive, "error", config=config)
    return results


def program_interface_controls(stimuli, scratch, environment):
    package = REPOSITORY / "eng" / "evaluation-tools" / "node_modules" / "@microsoft" / "vally"
    module = package / "dist" / "graders" / "static" / "program-grader.js"
    node = shutil.which("node")
    require(node is not None and module.is_file(), "installed Vally program grader and Node are required")
    version = json.loads(text(package / "package.json"))["version"]
    require(version == "0.14.0", "program-interface check requires the existing pinned Vally version")
    controls = []
    for index, label, outcome in ((0, "valid", "pass"), (1, "valid", "pass"),
                                  (0, "wrong-status", "fail"), (0, "tamper-criterion", "error")):
        controls.append({"case": CASES[index], "control": label, "outcome": outcome,
                         "config": stimuli[index]["graders"][1]["config"],
                         "workspace": str(scratch / "controls" / CASES[index] / label)})
    inputs = scratch / "program-interface-inputs.json"
    save(inputs, controls)
    adapter = """
import { readFileSync } from "node:fs";
import { pathToFileURL } from "node:url";
const { ProgramGrader } = await import(pathToFileURL(process.argv[1]).href);
const controls = JSON.parse(readFileSync(process.argv[2], "utf8"));
const results = [];
for (const control of controls) {
    const result = await new ProgramGrader().grade({
        config: control.config, trajectory: { workDir: control.workspace }
    });
    results.push({ case: control.case, control: control.control, result });
}
process.stdout.write(JSON.stringify(results));
"""
    run = command([node, "--input-type=module", "--eval", adapter, str(module), str(inputs)],
                  REPOSITORY, scratch / "program-interface.json", environment)
    require(run.returncode == 0, "Vally program-interface invocation failed; inspect retained log")
    results = json.loads(run.stdout)
    require(isinstance(results, list) and len(results) == 4, "missing program-interface result")
    for control, actual in zip(controls, results, strict=True):
        require((actual["case"], actual["control"]) == (control["case"], control["control"]),
                "program-interface control identity differs")
        check_grader_result(actual["result"], control["outcome"])
    return {"module": str(module), "module_sha256": hashlib.sha256(module.read_bytes()).hexdigest(),
            "version": version, "controls": results}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--scratch", type=Path, required=True)
    parser.add_argument("--graders-only", action="store_true",
                        help="Check grader changes without recompiling unchanged fixtures.")
    args = parser.parse_args()
    scratch = args.scratch.resolve()
    require(not scratch.is_relative_to(REPOSITORY), "scratch must be outside the repository")
    scratch.mkdir(parents=True, exist_ok=False)
    (scratch / "temp").mkdir()
    overrides = {"READINESS_REPOSITORY_ROOT": str(REPOSITORY), "READINESS_TEMP": str(scratch / "temp"),
                 "READINESS_TEST_ARTIFACTS": str(scratch / "controls"),
                 "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1",
                 "DOTNET_NOLOGO": "1", "TMP": str(scratch / "temp"), "TEMP": str(scratch / "temp")}
    environment = os.environ | overrides
    receipt = {"state": "incomplete", "repository": str(REPOSITORY), "environment": overrides,
               "global_json_sha256": digest(REPOSITORY / "global.json"), "eval_sha256": digest(EVAL),
               "helper_sha256": digest(Path(__file__)), "builds": {}, "controls": [],
               "compilation": "not-run (graders-only)" if args.graders_only else "required",
               "program_interface": {},
               "limit": "Fixed source/anchor checks, not general semantic reasoning or an assessor run."}
    try:
        spec = yaml.safe_load(text(EVAL))
        stimuli = check_fixture_and_spec(spec)
        receipt["sources"] = {p.relative_to(HERE).as_posix(): digest(p)
                              for p in HERE.rglob("*") if p.is_file()}
        receipt["staging"] = {CASES[i]: s["environment"]["files"] for i, s in enumerate(stimuli)}
        receipt["grader_sha256"] = hashlib.sha256(
            stimuli[0]["graders"][1]["config"]["args"][2].encode("utf-8")).hexdigest()
        if not args.graders_only:
            for case in CASES:
                receipt["builds"][case] = compile_case(case, scratch, environment)
            receipt["compilation"] = "passed"
        receipt["controls"] = grade_controls(stimuli, scratch, environment)
        receipt["program_interface"] = program_interface_controls(stimuli, scratch, environment)
        for index in range(2):
            for field in ("status", "anchors"):
                altered = copy.deepcopy(spec)
                config = altered["stimuli"][-2 + index]["graders"][1]["config"]
                expectation = json.loads(config["args"][3])
                expectation[field] = "verified" if field == "status" and index == 0 else (
                    "gap" if field == "status" else expectation["anchors"][1:])
                config["args"][3] = json.dumps(expectation)
                try:
                    check_fixture_and_spec(altered)
                except ValueError as error:
                    require("binding differs" in str(error), "unexpected config-integrity failure")
                else:
                    raise ValueError("changed grader label/locator binding was accepted")
        receipt["config_binding_negatives"] = 4
        require(receipt["sources"] == {p.relative_to(HERE).as_posix(): digest(p)
                                      for p in HERE.rglob("*") if p.is_file()},
                "fixture/helper bytes changed during checks")
        receipt["state"] = "passed"
        print(f"VALID BEQ-09 compilation {receipt['compilation']}; JSON program controls {len(receipt['controls'])}; "
              "Vally program-interface controls 4; config binding negatives 4; no assemblies or assessors executed")
    finally:
        save(scratch / "receipt.json", receipt)


if __name__ == "__main__":
    main()
