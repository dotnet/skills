"""Build trusted synthetic captures and exercise declared graders without model inference."""

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

import yaml

from controller import CASES, EVAL, FILES, HERE, NAMES, PROGRAM, digest, oracle, stimuli, text


REPOSITORY = HERE.parents[4]


def require(condition, message):
    if not condition:
        raise ValueError(message)


def save(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def run(argv, cwd, log):
    result = subprocess.run(argv, cwd=cwd, capture_output=True, text=True, encoding="utf-8",
                            errors="strict", timeout=180)
    save(log, {"argv": argv, "cwd": str(cwd), "exit_code": result.returncode,
               "stdout": result.stdout, "stderr": result.stderr})
    return result


def check_spec():
    spec = yaml.safe_load(text(EVAL))
    require(len(spec["stimuli"]) == 40, "expected original 38 plus exactly two controls")
    require(spec["defaults"] == {"runs": 1, "timeout": "20m"} and
            spec["scoring"] == {"threshold": 1.0}, "existing evaluation budget changed")
    require(len({s["name"] for s in spec["stimuli"]}) == 40, "duplicate identity")
    require(spec["stimuli"][38:] == stimuli(), "declared staging/grading differs from controller bindings")
    for f in FILES:
        a, b = text(HERE / CASES[0] / f), text(HERE / CASES[1] / f)
        if f == "EnvelopeCodec.cs":
            require(a.replace("writer.WriteRawValue(ScalarLexeme.Quote(reservation.Code), skipInputValidation: true);",
                              "writer.WriteStringValue(reservation.Code);") == b,
                    "pair differs beyond the one intended serialization operation")
        else:
            require(a == b, "shared source/layout differs")
    allowed = {"controller.py", "integrity.py", "README.md", "criterion.json"} | {
        f"{case}/{f}" for case in CASES for f in FILES}
    actual = {p.relative_to(HERE).as_posix() for p in HERE.rglob("*") if p.is_file() and
              "__pycache__" not in p.parts}
    require(actual == allowed, "unexpected repository fixture inventory")
    require(not any(p.is_symlink() for p in HERE.rglob("*")), "linked fixture")
    return spec["stimuli"][38:]


def worksheet(index, broad=False, qualified=False):
    expected = oracle(index)
    records = []
    for required in expected["paths"] + expected["excluded"]:
        record = {k: copy.deepcopy(v) for k, v in required.items() if k != "anchors"}
        citations = []
        if broad:
            for name in dict.fromkeys(a["path"] for a in required["anchors"]):
                lines = text(HERE / CASES[index] / name).splitlines()
                citations.append({"path": name, "start_line": 1, "end_line": len(lines),
                                  "quote": "\n".join(lines)})
        else:
            citations = [{"path": a["path"], "start_line": a["line"], "end_line": a["line"],
                          "quote": a["text"]} for a in required["anchors"]]
        record["citations"] = citations
        if qualified:
            if "route" in record:
                record["route"] = ["global::DockDispatch." + symbol for symbol in record["route"]]
                record["sink"] = "DockDispatch." + record["sink"]
                record["serialization"]["method"] = "System.Text.Json." + record["serialization"]["method"]
            else:
                record["entry"] = "DockDispatch." + record["entry"]
        records.append(record)
    return {"requirement_id": "BEQ-19", "status": expected["status"], "paths": records[:2],
            "excluded": records[2:], "limits": {"browser_executed": False,
                                              "exploitability_established": False,
                                              "application_ids_hostile": False}}


def controls(selected, scratch):
    results = []
    for index, stimulus in enumerate(selected):
        correct = worksheet(index)

        def grade(label, answer=correct, outcome="fail", mutate=None, config=None, raw=None):
            workspace = scratch / "controls" / CASES[index] / label
            workspace.mkdir(parents=True)
            for item in stimulus["environment"]["files"]:
                destination = workspace / item["dest"]
                destination.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(EVAL.parent / item["src"], destination)
            target = workspace / "out" / "serialization-decision.json"
            if answer is not None or raw is not None:
                target.parent.mkdir()
                target.write_bytes(raw if raw is not None else json.dumps(answer).encode())
            if mutate:
                mutate(workspace)
            config = config or stimulus["graders"][1]["config"]
            process = run([sys.executable, *config["args"]], workspace, workspace / "grader-log.json")
            require(process.returncode == 0, f"{label}: grader process failed")
            result = json.loads(process.stdout)
            require(result["status"] == ("error" if outcome == "error" else "success") and
                    result["passed"] is (outcome == "pass") and result["score"] == int(outcome == "pass"),
                    f"{CASES[index]}/{label}: expected {outcome}, got {result}")
            results.append({"case": CASES[index], "control": label, "outcome": outcome, "result": result,
                            "workspace": str(workspace), "config": config})

        grade("correct", outcome="pass")
        grade("broad-grounded-ranges", worksheet(index, broad=True), "pass")
        grade("qualified-symbols", worksheet(index, qualified=True), "pass")
        reordered = worksheet(index)
        reordered["paths"].reverse()
        grade("reordered-paths", reordered, "pass")
        for status in ("verified", "gap", "not tested", "owner evidence required", "not applicable"):
            if status != correct["status"]:
                altered = copy.deepcopy(correct)
                altered["status"] = status
                grade("conclusion-" + status.replace(" ", "-"), altered)
        grade("swapped-worksheet", worksheet(1 - index))
        for label, change in (
            ("ordinary-path-omitted", lambda a: a["paths"].pop(0)),
            ("secondary-path-omitted", lambda a: a["paths"].pop()),
            ("duplicate-branch", lambda a: a["paths"].__setitem__(1, a["paths"][0])),
            ("wrong-value", lambda a: a["paths"][1].__setitem__("value", "snapshot.DockId")),
            ("fabricated-route", lambda a: a["paths"][1]["route"].__setitem__(1, "ExternalRouter.Route")),
            ("unreachable-as-reachable", lambda a: a["paths"][1]["route"].__setitem__(1, "CalibrationExport.Emit")),
            ("wrong-sink", lambda a: a["paths"][1].__setitem__("sink", "Element.innerHTML")),
            ("wrong-writer", lambda a: a["paths"][1]["serialization"].__setitem__("method", "JsonSerializer.Serialize")),
            ("wrong-escaping", lambda a: a["paths"][1]["serialization"].__setitem__("escaping", "unresolved")),
            ("nonsense-explanation", lambda a: a.__setitem__("paths", "Because the moon is made of cheese.")),
            ("nonempty-nonsense-route", lambda a: a["paths"][1].__setitem__("route", ["the source looks fine"])),
            ("irrelevant-citations", lambda a: a["paths"][1].__setitem__("citations", a["paths"][0]["citations"])),
            ("fabricated-quote", lambda a: a["paths"][1]["citations"][0].__setitem__("quote", "typed and safe")),
            ("missing-exclusion", lambda a: a.__setitem__("excluded", [])),
            ("hostile-identifier-claim", lambda a: a["limits"].__setitem__("application_ids_hostile", True)),
            ("invented-browser-run", lambda a: a["limits"].__setitem__("browser_executed", True)),
        ):
            altered = copy.deepcopy(correct)
            change(altered)
            grade(label, altered)
        for name in FILES:
            grade("mutated-" + name, outcome="error", mutate=lambda w, name=name: (
                w / "fixture" / "component" / name).write_bytes(
                    (w / "fixture" / "component" / name).read_bytes() + b"\n"))
            grade("omitted-" + name, outcome="error", mutate=lambda w, name=name: (
                w / "fixture" / "component" / name).unlink())
        grade("mutated-criterion", outcome="error", mutate=lambda w: (
            w / "fixture" / "criterion.json").write_bytes(b"{}"))
        grade("extra-source", outcome="error", mutate=lambda w: (
            w / "fixture" / "component" / "Other.cs").write_bytes(b"// other"))
        grade("swapped-controller", outcome="error", config=selected[1 - index]["graders"][1]["config"])
        config = copy.deepcopy(stimulus["graders"][1]["config"])
        config["args"][3] = config["args"][3].replace('"status":"gap"', '"status":"verified"') if index == 0 else (
            config["args"][3].replace('"status":"verified"', '"status":"gap"'))
        grade("mutated-controller", outcome="error", config=config)
        grade("missing-output", answer=None)
        for label, raw in (("invalid-json", b"{"), ("invalid-utf8", b"\xff"), ("root-null", b"null"),
                           ("duplicate-key", json.dumps(correct).encode()[:-1] + b',"status":"gap"}')):
            grade(label, raw=raw)
    return results


PROBE = r'''using System.Reflection;
using System.Text.Json;

var assembly = Assembly.LoadFrom(args[0]);
var board = assembly.GetType("DockDispatch.DockBoard", true)!;
var baseType = board.BaseType!;
var publish = baseType.GetMethod("PublishAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
var channelField = baseType.GetField("channel", BindingFlags.Instance | BindingFlags.NonPublic)!;
var instance = Activator.CreateInstance(board)!;
foreach (var code in new[] { "batch-42", "unit\"\\\n<" })
{
    baseType.GetProperty("ReservationCode")!.SetValue(instance, code);
    baseType.GetProperty("Labels")!.SetValue(instance, new[] { "label\"\\\n<" });
    foreach (var queued in new[] { false, true })
    {
        baseType.GetProperty("QueueEnabled")!.SetValue(instance, queued);
        await (Task)publish.Invoke(instance, null)!;
        var channel = channelField.GetValue(instance)!;
        var bytes = (ReadOnlyMemory<byte>)channel.GetType().GetProperty("LastSent")!.GetValue(channel)!;
        if (!queued)
        {
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.GetProperty("Labels")[0].GetString() != "label\"\\\n<")
                throw new Exception("ordinary typed path did not preserve external text");
        }
        else if (args[1] == "typed" || code == "batch-42")
        {
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.GetProperty("reservation").GetString() != code)
                throw new Exception("reservation value did not round-trip");
            if (args[1] == "typed" && code != "batch-42" &&
                System.Text.Encoding.UTF8.GetString(bytes.Span).Contains('<'))
                throw new Exception("default encoder did not escape HTML-sensitive text");
        }
        else
        {
            try { using var document = JsonDocument.Parse(bytes); }
            catch (JsonException) { continue; }
            throw new Exception("raw scalar unexpectedly escaped the quoted value");
        }
    }
}
Console.WriteLine("PASS inherited button routes, normal typed snapshot and queued scalar boundary");
'''


def builds(scratch):
    dotnet = shutil.which("dotnet")
    require(dotnet is not None, "installed dotnet is required")
    offline = REPOSITORY / "plugins" / "dotnet-blazor" / "skills" / "blazor-component-readiness" / (
        "scripts/validator/restore-offline.config")
    common = [dotnet, "msbuild", "-noAutoResponse", "-verbosity:minimal",
              "-property:Configuration=Release", "-property:ImportDirectoryBuildProps=false",
              "-property:ImportDirectoryBuildTargets=false", "-property:ImportDirectoryPackagesProps=false",
              "-property:NuGetAudit=false", f"-property:RestoreConfigFile={offline}",
              f"-property:RestorePackagesPath={scratch / 'packages'}"]
    receipts = []
    for index, case in enumerate(CASES):
        output = scratch / "build" / case
        output.mkdir(parents=True)
        flags = [f"-property:BaseOutputPath={output / 'bin'}{os.sep}",
                 f"-property:BaseIntermediateOutputPath={output / 'obj'}{os.sep}",
                 "-property:EmitCompilerGeneratedFiles=true",
                 f"-property:CompilerGeneratedFilesOutputPath={output / 'generated'}"]
        command = common + [str(HERE / case / "DockDispatch.csproj")] + flags
        inventory = run(command + ["-target:ResolveRazorComponentInputs",
                                  "-getItem:Compile,RazorComponent,PackageReference,ProjectReference",
                                  "-getProperty:TargetFramework,NETCoreSdkVersion"],
                        REPOSITORY, output / "inventory.json")
        require(inventory.returncode == 0, "prebuild inventory failed")
        items = json.loads(inventory.stdout)
        require(items["Properties"]["TargetFramework"] == "net11.0", "wrong fixture framework")
        require({Path(p["FullPath"]).resolve() for p in items["Items"]["Compile"]} == {
            HERE / case / f for f in FILES if f.endswith(".cs")}, "fixture Compile closure differs")
        require({Path(p["FullPath"]).resolve() for p in items["Items"]["RazorComponent"]} == {
            HERE / case / "DockBoard.razor"}, "fixture Razor closure differs")
        require(not items["Items"]["PackageReference"] and not items["Items"]["ProjectReference"],
                "unexpected executable dependency")
        build = run(command + ["-target:Build"], REPOSITORY, output / "build-initial.json")
        if build.returncode:
            require("NETSDK1004" in build.stdout + build.stderr, "build failed for non-assets reason")
            restore = run(command + ["-target:Restore"], REPOSITORY, output / "restore-offline.json")
            require(restore.returncode == 0, "offline restore failed")
            build = run(command + ["-target:Build"], REPOSITORY, output / "build.json")
        require(build.returncode == 0, "fixture build failed")
        generated = list((output / "generated").rglob("*DockBoard_razor.g.cs"))
        require(len(generated) == 1, "missing/ambiguous generated component")
        code = re.sub(r"(?m)^[ \t\ufeff]*#(?:line\b|nullable\b|pragma (?:checksum|warning)\b)[^\n]*",
                      "", text(generated[0]))
        require(re.search(r"class DockBoard\s*:\s*DispatchPanelBase", code) is not None,
                "generated inheritance differs")
        require(re.search(r'"onclick"\s*,\s*(?:global::)?Microsoft\.AspNetCore\.Components\.EventCallback'
                          r'\.Factory\.Create<(?:global::)?Microsoft\.AspNetCore\.Components\.Web\.MouseEventArgs>'
                          r'\s*\(\s*this\s*,\s*PublishAsync\s*\)', code) is not None and '"@onclick"' not in code,
                "button is not a typed event binding")
        assembly = output / "bin" / "Release" / "net11.0" / "DockDispatch.dll"
        require(assembly.is_file(), "fixture assembly missing")
        receipts.append({"case": case, "sdk": items["Properties"]["NETCoreSdkVersion"],
                         "assembly": str(assembly), "generated_sha256": digest(generated[0])})
    probe = scratch / "probe"
    probe.mkdir()
    (probe / "Program.cs").write_text(PROBE, encoding="utf-8")
    project = probe / "Probe.csproj"
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
                       '<TargetFramework>net11.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings>'
                       '<Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors>'
                       '</PropertyGroup><ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App" />'
                       '</ItemGroup></Project>', encoding="utf-8")
    command = common + [str(project)]
    build = run(command + ["-target:Build"], REPOSITORY, probe / "build-initial.json")
    if build.returncode:
        require("NETSDK1004" in build.stdout + build.stderr, "probe failed for non-assets reason")
        restore = run(command + ["-target:Restore"], REPOSITORY, probe / "restore-offline.json")
        require(restore.returncode == 0, "probe offline restore failed")
        build = run(command + ["-target:Build"], REPOSITORY, probe / "build.json")
    require(build.returncode == 0, "trusted controller probe did not build")
    for index, receipt in enumerate(receipts):
        process = run([dotnet, str(probe / "bin" / "Release" / "net11.0" / "Probe.dll"),
                       receipt["assembly"], "raw" if index == 0 else "typed"],
                      REPOSITORY, probe / f"{CASES[index]}.json")
        require(process.returncode == 0 and process.stdout.startswith("PASS"), "trusted routing probe failed")
        receipt["trusted_probe"] = process.stdout.strip()
    return receipts


def program_interface(results, scratch):
    package = REPOSITORY / "eng" / "evaluation-tools" / "node_modules" / "@microsoft" / "vally"
    module = package / "dist" / "graders" / "static" / "program-grader.js"
    require(module.is_file(), "installed pinned Vally ProgramGrader is required; no inference is run")
    require(json.loads(text(package / "package.json"))["version"] == "0.14.0", "unexpected Vally version")
    selected = [r for r in results if r["control"] in ("correct", "conclusion-not-tested", "mutated-criterion")]
    inputs = scratch / "interface-inputs.json"
    save(inputs, selected)
    code = """
import { readFileSync } from "node:fs";
import { pathToFileURL } from "node:url";
const { ProgramGrader } = await import(pathToFileURL(process.argv[1]).href);
const controls = JSON.parse(readFileSync(process.argv[2], "utf8"));
const results = [];
for (const c of controls) {
  const r = await new ProgramGrader().grade({config: c.config, trajectory: {workDir: c.workspace}});
  if (r.status !== c.result.status || r.passed !== c.result.passed || r.score !== c.result.score)
    throw new Error("ProgramGrader interface classification differs");
  results.push({case: c.case, control: c.control, result: r});
}
console.log(JSON.stringify(results));
"""
    process = run(["node", "--input-type=module", "--eval", code, str(module), str(inputs)],
                  REPOSITORY, scratch / "program-interface.json")
    require(process.returncode == 0, "ProgramGrader adapter check failed")
    return json.loads(process.stdout)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--scratch", required=True, type=Path)
    args = parser.parse_args()
    scratch = args.scratch.resolve()
    require(not scratch.is_relative_to(REPOSITORY), "scratch must be outside the repository")
    scratch.mkdir(parents=True, exist_ok=False)
    receipt = {"state": "incomplete", "eval_sha256": digest(EVAL),
               "scope": "Two correlated controls of one serialization mechanism; no model-performance claim.",
               "limits": "Fixed source-bound structured relationships; independent prompt rubric judges natural "
                         "semantic explanation. Local checks do not prove arbitrary prose correctness or skill benefit."}
    try:
        selected = check_spec()
        receipt["sources"] = {p.relative_to(HERE).as_posix(): digest(p) for p in HERE.rglob("*")
                              if p.is_file() and "__pycache__" not in p.parts}
        receipt["builds"] = builds(scratch)
        result = controls(selected, scratch)
        receipt["controls"] = [{k: v for k, v in r.items() if k != "config"} for r in result]
        receipt["program_interface"] = program_interface(result, scratch)
        receipt["state"] = "passed"
        print(f"PASS {len(result)} adversarial controls, both synthetic builds/routing probes, "
              f"{len(receipt['program_interface'])} real ProgramGrader interface controls")
    finally:
        save(scratch / "receipt.json", receipt)


if __name__ == "__main__":
    main()
