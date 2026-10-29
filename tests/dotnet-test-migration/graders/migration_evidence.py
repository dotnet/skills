"""Fixture-scoped migration evidence, not a general-purpose test-result tool."""

import argparse
import copy
import hashlib
import json
import os
from pathlib import Path, PureWindowsPath
import re
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as ET


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


HOST_STATE = {".git", ".agents", ".copilot", ".claude"}


def preserve(root, expected, workspace=False):
    root = Path(root)
    assert not is_linked_path(root), f"Symlink or junction in preserved scope: {root}"
    paths = [
        path for path in root.rglob("*")
        if not workspace or path.relative_to(root).parts[0] not in HOST_STATE
    ]
    assert not any(is_linked_path(path) for path in paths), (
        f"Symlink or junction in preserved scope: {root}"
    )
    actual = {
        path.relative_to(root).as_posix(): sha256(path)
        for path in paths if path.is_file()
        and not (workspace and path.suffix == ".pyc" and "__pycache__" in path.relative_to(root).parts)
    }
    assert actual == expected, {"expected": expected, "actual": actual}


def read_trx(path):
    root = ET.parse(path).getroot()
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    results = root.findall(".//t:UnitTestResult", ns)
    cases = {}
    for result in results:
        name = result.attrib["testName"]
        assert name not in cases, f"Duplicate case identity: {name}"
        cases[name] = {
            "outcome": result.attrib["outcome"],
            "message": "\n".join(
                node.text or "" for node in result.findall(".//t:Message", ns)
            ),
        }
    counters = root.find(".//t:ResultSummary/t:Counters", ns)
    assert counters is not None, "Missing TRX counters"
    assert int(counters.attrib["total"]) == len(cases), (
        "Result counters disagree with case records", counters.attrib, cases
    )
    return cases


def verify_cases(actual, expected, skip_reasons):
    outcomes = {name: case["outcome"] for name, case in actual.items()}
    assert outcomes == expected, {"expected": expected, "actual": outcomes}
    for name, reason in skip_reasons.items():
        assert reason in actual[name]["message"], (name, actual[name])


def invariant_source(source, profile):
    tokens = []
    pattern = re.compile(
        r'(?P<comment>//[^\r\n]*|/\*.*?\*/)|'
        r'(?P<string>(?:\$@|@\$|@)"(?:[^"]|"")*"|\$?"(?:\\.|[^"\\])*"|'
        r"'(?:\\.|[^'\\])*')|(?P<space>\s+)|"
        r'(?P<token>[A-Za-z_]\w*|\d+(?:\.\d+)?|=>|==|!=|<=|>=|\+\+|--|&&|\|\||\?\?|\S)',
        re.S,
    )
    for match in pattern.finditer(source):
        if match.lastgroup not in {"comment", "space"}:
            tokens.append(match[0])
    replacements = [
        (("[", name, "(", ")", "]"), ("[", name, "]"))
        for name in ("TestClass", "TestMethod", "DataTestMethod", "ClassInitialize", "ClassCleanup")
    ] + [
        (("Assert", ".", "ThrowsExactly"), ("Assert", ".", "ThrowsException")),
        (("[", "DataTestMethod", "]"), ("[", "TestMethod", "]")),
    ]
    if profile == "mstest-upgrade":
        replacements += [
            (("[", "ClassCleanup", "(", "ClassCleanupBehavior", ".", "EndOfClass", ")", "]"),
             ("[", "ClassCleanup", "]")),
            (("TestTimeout", ".", "Infinite"), ("int", ".", "MaxValue")),
        ]
    elif profile == "collection-tests":
        replacements.append((("Assert", ".", "Equal"), ("Assert", ".", "AreEqual")))
    else:
        assert profile == "staged-orders", f"Unknown source contract: {profile}"
    for before, after in replacements:
        index = 0
        while index <= len(tokens) - len(before):
            if tuple(tokens[index:index + len(before)]) == before:
                tokens[index:index + len(before)] = after
                index += len(after)
            else:
                index += 1
    if profile == "collection-tests":
        bodies = []
        for name in ("FirstCase", "SecondCase"):
            prefix = (name, "(", ")", "{")
            matches = [i for i in range(len(tokens) - 3)
                       if tuple(tokens[i:i + 4]) == prefix]
            assert len(matches) == 1, f"Missing or duplicate test body: {name}"
            start = matches[0] + 3
            depth = 1
            end = start + 1
            while end < len(tokens) and depth:
                depth += (tokens[end] == "{") - (tokens[end] == "}")
                end += 1
            assert depth == 0, f"Unclosed test body: {name}"
            bodies.append((name, tokens[start:end]))
        tokens = bodies
    return hashlib.sha256(json.dumps(tokens, separators=(",", ":")).encode("utf-8")).hexdigest()


def verify_sources(root, contracts):
    verified = {}
    for contract in contracts:
        profile, expected, filename = contract.split(":", 2)
        path = root / filename
        assert path.is_file() and not path.is_symlink(), f"Missing invariant source: {filename}"
        actual = invariant_source(path.read_text(encoding="utf-8"), profile)
        assert actual == expected, f"Source invariant changed: {filename} ({profile})"
        verified[filename] = {"profile": profile, "normalized_sha256": actual}
    return verified


def verify_unchanged_files(root, contracts):
    verified = {}
    for item in contracts:
        expected, filename = item.split(":", 1)
        path = root / filename
        assert not path.is_symlink() and path.is_file(), filename
        actual = hashlib.sha256(path.read_bytes().replace(b"\r\n", b"\n")).hexdigest()
        assert actual == expected, f"Preserved fixture changed: {filename}"
        verified[filename] = actual
    return verified


def is_linked_path(path):
    return path.is_symlink() or getattr(path, "is_junction", lambda: False)()


def configuration_snapshot(root):
    snapshot = {}
    for path in root.rglob("*"):
        relative = path.relative_to(root)
        if (relative.parts[0] in HOST_STATE or {"bin", "obj"}.intersection(relative.parts)
                or relative.parts[:2] == (".eval", "results")):
            continue
        if path.is_file():
            compiler_path(root, str(path), root)
            snapshot[relative.as_posix()] = sha256(path)
    return snapshot


def verify_configuration(root, expected, package_assets=None):
    actual = configuration_snapshot(root)
    if package_assets:
        expected = {**expected, **package_assets}
        for name in package_assets:
            path = compiler_path(root, name, root)
            if path.is_file():
                actual[name] = sha256(path)
    assert actual == expected, (
        "Migration configuration changed",
        {"added": sorted(actual.keys() - expected.keys()),
         "removed": sorted(expected.keys() - actual.keys()),
         "changed": sorted(name for name in actual.keys() & expected.keys()
                           if actual[name] != expected[name])},
    )


def validate_checkpoint(root):
    assert root.is_dir() and not is_linked_path(root), f"Invalid checkpoint root: {root}"
    for path in root.rglob("*"):
        assert not is_linked_path(path), f"Checkpoint symlink or junction: {path}"
        if not path.is_file() or path.suffix not in {".csproj", ".props", ".targets"}:
            continue
        if {"obj", "bin", ".eval", ".git"}.intersection(path.relative_to(root).parts):
            continue
        for item in ET.parse(path).getroot().iter():
            tag = item.tag.rsplit("}", 1)[-1]
            key = "Project" if tag == "Import" else "Include"
            if tag not in {"Compile", "ProjectReference", "Import"} or key not in item.attrib:
                continue
            for value in item.attrib[key].split(";"):
                value = value.strip().replace("\\", "/")
                assert value and not any(marker in value for marker in ("$(", "@(", "%(")), (
                    f"Checkpoint reference must be literal and self-contained: {path}: {value}"
                )
                assert not PureWindowsPath(value).drive and not Path(value).is_absolute(), (
                    f"External checkpoint reference: {path}: {value}"
                )
                resolved = (path.parent / value).resolve()
                assert resolved.is_relative_to(root.resolve()), (
                    f"External checkpoint reference: {path}: {value}"
                )


def execute(command, root, evidence, name, environment=None, expected_exit=0):
    result = subprocess.run(
        command, cwd=root, env=environment, capture_output=True, text=True,
        encoding="utf-8", errors="replace", timeout=150,
    )
    (evidence / f"{name}.stdout.txt").write_text(result.stdout, encoding="utf-8")
    (evidence / f"{name}.stderr.txt").write_text(result.stderr, encoding="utf-8")
    assert result.returncode == expected_exit, (
        command, result.returncode, result.stdout, result.stderr
    )
    return result


def verify_mstest_packages(assets, project, major=None, version=None):
    resolved = {}
    for package in ("MSTest.TestFramework", "MSTest.TestAdapter"):
        matches = [key.split("/", 1)[1] for key in assets if key.startswith(package + "/")]
        assert len(matches) == 1, (str(project), package, matches)
        resolved[package] = matches[0]
        if major:
            assert matches[0].split(".")[0] == major, (str(project), package, matches)
        if version:
            assert matches[0] == version, (str(project), package, matches)
    assert len(set(resolved.values())) == 1, (str(project), resolved)


def verify_runner(effective, native, runner):
    assert effective["TargetFramework"] == "net8.0", effective
    bridge = effective["TestingPlatformDotnetTestSupport"].lower() == "true"
    mtp = any(effective[name].lower() == "true" for name in (
        "EnableMSTestRunner", "UseMicrosoftTestingPlatformRunner",
        "IsTestingPlatformApplication",
    ))
    assert native == (runner == "native"), effective
    if runner == "vstest":
        assert not bridge and not mtp, effective
    else:
        assert mtp and effective["OutputType"] == "Exe", effective
        assert bridge == (runner == "bridge"), effective


def compiler_path(directory, value, root=None):
    path = directory / value.strip('"').replace("\\", "/")
    if root is not None:
        # Inspect links before resolving their targets. Resolve only each link's
        # parent for scope comparisons so Windows short/long names are equivalent.
        assert not is_linked_path(root) and not any(
            is_linked_path(parent) and parent.parent.resolve().is_relative_to(root.resolve())
            for parent in (path, *path.parents)
        ), (
            "Compiler input contract source uses a symlink or junction", str(path)
        )
    return path.resolve()


def compile_contracts(root, contracts, projects):
    allowed = {}
    for contract in contracts:
        project, source = contract.split("=", 1)
        paths = []
        for value in (project, source):
            assert not PureWindowsPath(value).drive and not Path(value).is_absolute(), (
                f"Compiler input contract paths must be workspace-relative: {value}"
            )
            path = compiler_path(root, value, root)
            assert path.is_relative_to(root.resolve()) and path.is_file(), (
                f"Compiler input contract path is missing or external: {value}"
            )
            paths.append(path)
        allowed.setdefault(paths[0], set()).add(paths[1])
    if contracts:
        assert set(allowed) == {compiler_path(root, project) for project in projects}, (
            "Compiler input contract must cover every test and production project", projects
        )
    return allowed


def verify_compile_inputs(root, project, expected, build):
    project = compiler_path(root, str(project), root)
    expected = {compiler_path(root, str(path), root) for path in expected}
    directory = project.parent
    properties = build["Properties"]
    sdk = Path(properties["MSBuildToolsPath"]).resolve()
    packages = Path(properties["NuGetPackageRoot"]).resolve()
    assert not sdk.is_relative_to(root.resolve()) and not packages.is_relative_to(root.resolve()), (
        "Compiler input contract requires external SDK and package generation targets"
    )
    intermediate = directory / "obj" / "Debug" / "net8.0"
    generated = {
        intermediate / ".NETCoreApp,Version=v8.0.AssemblyAttributes.cs":
            sdk / "Microsoft.Common.CurrentVersion.targets",
        intermediate / f"{project.stem}.AssemblyInfo.cs":
            sdk / "Sdks" / "Microsoft.NET.Sdk" / "targets" / "Microsoft.NET.GenerateAssemblyInfo.targets",
        intermediate / f"{project.stem}.GlobalUsings.g.cs":
            sdk / "Sdks" / "Microsoft.NET.Sdk" / "targets" / "Microsoft.NET.GenerateGlobalUsings.targets",
    }
    assets = json.loads((directory / "obj" / "project.assets.json").read_text(encoding="utf-8"))
    for package in assets["libraries"]:
        name, version = package.split("/", 1)
        if name.lower() == "microsoft.net.test.sdk":
            target = packages / name.lower() / version / "build" / "netcoreapp3.1"
            generated[target / "Microsoft.NET.Test.Sdk.Program.cs"] = target / "Microsoft.NET.Test.Sdk.targets"
        if name.lower() == "microsoft.testing.platform.msbuild":
            target = (packages / name.lower() / version / "buildMultiTargeting" /
                      "Microsoft.Testing.Platform.MSBuild.targets")
            for filename in ("MicrosoftTestingPlatformEntryPoint.cs", "TestPlatformEntryPoint.cs",
                             "SelfRegisteredExtensions.cs"):
                generated[intermediate / filename] = target
    generated = {path.resolve(): target.resolve() for path, target in generated.items()}
    items = build["Items"]
    compile_items = {
        compiler_path(directory, item["FullPath"], root): item
        for item in items.get("Compile", [])
    }
    # Csc's emitted arguments bind the check to the compilation, not evaluation or a
    # Compile list restored by an AfterTargets hook. No arbitrary obj/Generated exemption.
    sources = set()
    for item in items.get("CscCommandLineArgs", []):
        value = item["Identity"].strip('"')
        assert not value.startswith("@"), "Compiler input contract forbids response-file indirection"
        if not re.match(r"^[/-][A-Za-z][A-Za-z0-9]*[+-]?(?::|$)", value):
            sources.add(compiler_path(directory, value, root))
    assert sources and sources == set(compile_items), (
        "Compiler input contract disagrees with actual Csc inputs", str(project),
        sorted(map(str, sources)), sorted(map(str, compile_items)),
    )
    missing = expected - sources
    extra = {
        path for path in sources - expected
        if path not in generated or compiler_path(
            directory, compile_items[path].get("DefiningProjectFullPath", "")
        ) != generated[path]
    }
    assert not missing and not extra, (
        "Compiler input contract", str(project),
        {"missing": sorted(map(str, missing)), "uncontracted": sorted(map(str, extra))},
    )
    for path in sources:
        assert path.is_file() and (path.is_relative_to(root.resolve()) or path in generated), (
            "Compiler input contract has missing or external source", str(path)
        )
    return {
        (path.relative_to(root.resolve()).as_posix()
         if path.is_relative_to(root.resolve()) else path.as_posix()): sha256(path)
        for path in sorted(sources)
    }


def verify_project_inventory(root, projects):
    actual = {
        compiler_path(root, str(path), root) for path in root.rglob("*")
        if path.is_file() and path.suffix.lower().endswith("proj")
    }
    assert actual == set(projects), (
        "Project reference contract: uncontracted or missing local projects",
        sorted(map(str, actual ^ set(projects))),
    )


def verify_project_graph(root, builds, production_projects):
    verify_project_inventory(root, builds)
    production = {compiler_path(root, name, root) for name in production_projects}
    outputs = {}
    for project, build in builds.items():
        values = build["Properties"]
        outputs[project] = {
            key: compiler_path(project.parent, values[key], root)
            for key in ("TargetPath", "TargetRefPath")
        }
        for key in ("TargetPath", "TargetRefPath") if project in production else ("TargetPath",):
            path = outputs[project][key]
            assert path.is_relative_to(root.resolve()) and path.is_file(), (
                "Project reference contract: missing or external output", str(path)
            )
    graph = {}
    for project, build in builds.items():
        expected = set() if project in production else production
        items = build["Items"]
        actual = {
            compiler_path(project.parent, item["FullPath"], root)
            for item in items.get("ProjectReference", [])
        }
        assert actual == expected, ("Project reference contract: project edges", str(project), actual, expected)
        references = set()
        global_references = set()
        for item in items.get("CscCommandLineArgs", []):
            match = re.fullmatch(r"[/-](?:reference|r):(.+)", item["Identity"], re.I)
            if match:
                value = match[1].strip('"')
                aliases, value = value.split("=", 1) if "=" in value else ("global", value)
                path = compiler_path(project.parent, value, root)
                references.add(path)
                if "global" in aliases.split(","):
                    global_references.add(path)
        expected_references = {outputs[dependency]["TargetRefPath"] for dependency in expected}
        local_references = {path for path in references if path.is_relative_to(root.resolve())}
        assert local_references == expected_references and expected_references <= global_references, (
            "Project reference contract: compiler references", str(project),
            sorted(map(str, local_references)), sorted(map(str, expected_references)),
        )
        runtime = {}
        for dependency in expected:
            bindings = [
                item for item in items.get("ReferencePath", [])
                if item.get("ReferenceSourceTarget") == "ProjectReference"
                and item.get("MSBuildSourceProjectFile")
                and compiler_path(project.parent, item["MSBuildSourceProjectFile"], root) == dependency
            ]
            assert len(bindings) == 1, (
                "Project reference contract: missing production metadata", str(project), str(dependency)
            )
            binding = bindings[0]
            for metadata, output in (("FullPath", "TargetPath"), ("ReferenceAssembly", "TargetRefPath")):
                assert binding.get(metadata) and compiler_path(
                    project.parent, binding[metadata], root
                ) == outputs[dependency][output], (
                    "Project reference contract: substituted production metadata", str(project), binding
                )
            original = outputs[dependency]["TargetPath"]
            copied = outputs[project]["TargetPath"].parent / original.name
            assert copied.is_file() and sha256(copied) == sha256(original), (
                "Project reference contract: substituted runtime dependency", str(copied)
            )
            runtime[copied.name] = sha256(copied)
        graph[project.relative_to(root.resolve()).as_posix()] = {
            "projects": [path.relative_to(root.resolve()).as_posix() for path in sorted(expected)],
            "compiler_references": [
                path.relative_to(root.resolve()).as_posix() for path in sorted(expected_references)
            ],
            "runtime_dependencies": runtime,
        }
    return graph


def run(args, root=None):
    root = Path.cwd() if root is None else root
    configuration = configuration_snapshot(root)
    source_invariants = verify_sources(root, args.source_contract)
    preserved = verify_unchanged_files(root, args.unchanged_file)
    artifacts = root / ".eval" / "results"
    artifacts.mkdir(parents=True, exist_ok=True)
    evidence = Path(tempfile.mkdtemp(prefix="migration-", dir=artifacts))
    project = Path(args.project)
    assert (root / project).is_file(), project
    assert args.expect or args.allow_empty, "An empty run needs explicit authorization"
    execute(["dotnet", "restore", str(project)], root, evidence, "restore")
    verify_configuration(root, configuration)
    properties = (
        "TargetFramework,OutputType,EnableMSTestRunner,"
        "UseMicrosoftTestingPlatformRunner,TestingPlatformDotnetTestSupport,"
        "IsTestingPlatformApplication,UseVSTest,Nullable"
    )
    evaluation = execute(
        ["dotnet", "msbuild", str(project), "-getProperty:" + properties],
        root, evidence, "properties",
    )
    effective = json.loads(evaluation.stdout)["Properties"]
    native = json.loads((root / "global.json").read_text(encoding="utf-8")).get(
        "test", {}
    ).get("runner") == "Microsoft.Testing.Platform"
    verify_runner(effective, native, args.runner)
    production = {}
    for index, filename in enumerate(args.production_project):
        values = json.loads(execute(
            ["dotnet", "msbuild", filename, "-getProperty:" + properties],
            root, evidence, f"production-{index}",
        ).stdout)["Properties"]
        assert values["OutputType"] == "Library", values
        assert values["TargetFramework"] == "net8.0", (filename, values)
        assert values["Nullable"] == "enable", (filename, values)
        for name in ("EnableMSTestRunner", "UseMicrosoftTestingPlatformRunner",
                     "TestingPlatformDotnetTestSupport", "IsTestingPlatformApplication"):
            assert values[name].lower() != "true", (filename, values)
        production[filename] = values

    package_projects = {}
    package_assets = {}
    test_project_properties = {str(project): effective}
    for index, filename in enumerate(dict.fromkeys([str(project), *args.package_project])):
        if index:
            execute(["dotnet", "restore", filename], root, evidence, f"restore-package-{index}")
            verify_configuration(root, configuration, package_assets)
            values = json.loads(execute(
                ["dotnet", "msbuild", filename, "-getProperty:" + properties],
                root, evidence, f"test-properties-{index}",
            ).stdout)["Properties"]
            verify_runner(values, native, args.runner)
            test_project_properties[filename] = values
        assets_path = root / Path(filename).parent / "obj" / "project.assets.json"
        assets = json.loads(assets_path.read_text(encoding="utf-8"))["libraries"]
        if args.mstest_major or args.mstest_version:
            verify_mstest_packages(assets, filename, args.mstest_major, args.mstest_version)
        package_projects[filename] = sorted(assets)
        package_assets[assets_path.relative_to(root).as_posix()] = sha256(assets_path)
    contracts = compile_contracts(
        root, args.compile_source, [*package_projects, *args.production_project]
    )
    if contracts and args.production_project:
        verify_project_inventory(root, contracts)
    for project_path in contracts:
        assets_path = project_path.parent / "obj" / "project.assets.json"
        package_assets.setdefault(
            assets_path.relative_to(root.resolve()).as_posix(), sha256(assets_path)
        )
    compiler_inputs = {}
    builds = {}
    for index, (filename, expected_sources) in enumerate(contracts.items()):
        build = json.loads(execute(
            ["dotnet", "msbuild", str(filename), "-t:Rebuild", "-p:ProvideCommandLineArgs=true",
             "-getItem:Compile,CscCommandLineArgs,ProjectReference,ReferencePath",
             "-getProperty:MSBuildToolsPath,NuGetPackageRoot,TargetPath,TargetRefPath"],
            root, evidence, f"compile-{index}",
        ).stdout)
        verify_unchanged_files(root, args.unchanged_file)
        verify_sources(root, args.source_contract)
        verify_configuration(root, configuration, package_assets)
        compiler_inputs[filename.relative_to(root.resolve()).as_posix()] = verify_compile_inputs(
            root, filename, expected_sources, build
        )
        builds[filename] = build
    project_graph = verify_project_graph(root, builds, args.production_project) if (
        builds and args.production_project
    ) else {}
    target = args.solution or str(project)
    no_build = ["--no-build"] if contracts else []
    if args.runner == "vstest":
        command = [
            "dotnet", "test", target, *no_build, "--logger",
            "trx", "--results-directory", str(evidence),
        ]
    else:
        command = ["dotnet", "test"]
        command += [
            "--solution" if args.solution else "--project", target
        ] + no_build if native else [target, *no_build, "--"]
        command += [
            "--report-trx", "--results-directory", str(evidence),
        ]
    command += args.runner_arg
    environment = dict(os.environ, MIGRATION_PROBE=str(evidence / "lifecycle.jsonl"))
    result = execute(
        command, root, evidence, "test", environment, args.exit_code,
    )
    verify_configuration(root, configuration, package_assets)
    reports = list(evidence.rglob("*.trx"))
    assert len(reports) == args.report_count, (
        f"Expected {args.report_count} fresh reports, found {reports}"
    )
    cases = {}
    for report in reports:
        module_cases = read_trx(report)
        assert not cases.keys() & module_cases.keys(), "Ambiguous cross-module case identity"
        cases.update(module_cases)
    expected = dict(value.rsplit("=", 1) for value in args.expect)
    reasons = dict(value.split("=", 1) for value in args.skip_reason)
    verify_cases(cases, expected, reasons)
    verify_unchanged_files(root, args.unchanged_file)
    verify_sources(root, args.source_contract)
    if project_graph:
        verify_project_graph(root, builds, args.production_project)
    if args.lifecycle:
        events = [
            json.loads(line) for line in (evidence / "lifecycle.jsonl").read_text(
                encoding="utf-8"
            ).splitlines()
        ]
        assert [event["event"] for event in events].count("created") == 1, events
        assert [event["event"] for event in events].count("disposed") == 1, events
        assert events[0]["event"] == "created" and events[-1]["event"] == "disposed", events
        assert len({event["instance"] for event in events}) == 1, events
        entered = [event["case"] for event in events if event["event"] == "entered"]
        exited = [event["case"] for event in events if event["event"] == "exited"]
        passed = {name for name, outcome in expected.items() if outcome == "Passed"}
        assert set(entered) == set(exited) == passed, (events, passed)
        assert len(entered) == len(exited) == len(passed), events
        active = None
        for event in events:
            if event["event"] == "entered":
                assert active is None, f"Overlapping collection members: {events}"
                active = event["case"]
            elif event["event"] == "exited":
                assert active == event["case"], events
                active = None
        assert active is None
    summary = {
        "runner": args.runner, "project": str(project), "command": command,
        "exit_code": result.returncode, "properties": effective,
        "production_properties": production,
        "test_project_properties": test_project_properties,
        "packages": package_projects[str(project)], "package_projects": package_projects,
        "cases": cases,
        "preserved_files": preserved,
        "source_invariants": source_invariants,
        "compiler_inputs": compiler_inputs,
        "project_reference_graph": project_graph,
        "verified_input_files": configuration,
        "verified_package_assets": package_assets,
        "reports": {str(report): sha256(report) for report in reports},
    }
    (evidence / "evidence.json").write_text(
        json.dumps(summary, indent=2), encoding="utf-8"
    )
    print(json.dumps(summary))
    return summary


def run_checkpoints(args):
    root = Path.cwd()
    assert not is_linked_path(root / "checkpoints"), "Checkpoint parent must not be a symlink or junction"
    (root / ".eval" / "results").mkdir(parents=True, exist_ok=True)
    checkpoints = (
        ("v3-vstest", root / "checkpoints" / "v3", "vstest", "3"),
        ("v4-vstest", root / "checkpoints" / "v4", "vstest", "4"),
        ("v4-native", root, "native", "4"),
    )
    evidence = {}
    for name, directory, runner, major in checkpoints:
        assert directory.is_dir(), f"Missing replayable migration checkpoint: {name}"
        stage = copy.copy(args)
        stage.runner = runner
        stage.mstest_major = major
        stage.mstest_version = None
        if directory == root:
            evidence[name] = run(stage, directory)
            continue
        validate_checkpoint(directory)
        with tempfile.TemporaryDirectory(prefix="isolated-migration-checkpoint-") as temporary:
            isolated = Path(temporary) / "snapshot"
            assert not isolated.resolve().is_relative_to(root.resolve()), (
                "Checkpoint replay requires a temporary directory outside the delivered workspace"
            )
            shutil.copytree(directory, isolated, ignore=shutil.ignore_patterns("bin", "obj", ".eval", ".git"))
            result = run(stage, isolated)
            destination = Path(tempfile.mkdtemp(
                prefix=f"{name}-", dir=root / ".eval" / "results",
            ))
            evidence_root = next(iter(result["reports"]))
            shutil.copytree(Path(evidence_root).parent, destination, dirs_exist_ok=True)
            result["reports"] = {
                str(destination / Path(path).name): digest
                for path, digest in result["reports"].items()
            }
            result["checkpoint_source"] = str(directory)
            result["isolated_workspace_retained"] = False
            (destination / "evidence.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
            evidence[name] = result
    print(json.dumps({"verified_checkpoints": evidence}))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="operation", required=True)
    check = commands.add_parser("preserve")
    check.add_argument("--root", required=True)
    check.add_argument("--file", action="append", default=[], required=True)
    check.add_argument("--workspace", action="store_true")
    test = commands.add_parser("run", aliases=["stages"])
    test.add_argument("--project", required=True)
    test.add_argument("--solution")
    test.add_argument("--report-count", type=int, default=1)
    test.add_argument("--runner", choices=["vstest", "bridge", "native"], required=True)
    test.add_argument("--expect", action="append", default=[])
    test.add_argument("--skip-reason", action="append", default=[])
    test.add_argument("--mstest-major")
    test.add_argument("--mstest-version")
    test.add_argument("--production-project", action="append", default=[])
    test.add_argument("--package-project", action="append", default=[])
    test.add_argument("--lifecycle", action="store_true")
    test.add_argument("--allow-empty", action="store_true")
    test.add_argument("--exit-code", type=int, default=0)
    test.add_argument("--runner-arg", action="append", default=[])
    test.add_argument("--unchanged-file", action="append", default=[])
    test.add_argument("--source-contract", action="append", default=[])
    test.add_argument("--compile-source", action="append", default=[], metavar="PROJECT=SOURCE")
    args = parser.parse_args()
    if args.operation == "preserve":
        preserve(args.root, {
            path: digest for digest, path in (
                value.split(":", 1) for value in args.file
            )
        }, workspace=args.workspace)
        print("PRESERVED: complete file set and bytes")
    elif args.operation == "stages":
        run_checkpoints(args)
    else:
        run(args)


if __name__ == "__main__":
    main()
