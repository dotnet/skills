import importlib.util
import copy
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest


SPEC = importlib.util.spec_from_file_location(
    "migration_evidence", Path(__file__).with_name("migration_evidence.py")
)
evidence = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(evidence)


class EvidenceTests(unittest.TestCase):
    def test_configuration_snapshot_rejects_rewrites_additions_and_removals(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            filenames = ("Tests.csproj", "Directory.Build.props", "Directory.Build.targets",
                         "Directory.Packages.props", "global.json", "NuGet.Config", "test.runsettings",
                         "Directory.Build.rsp", ".editorconfig", "custom.settings.json", "tests.cs")
            for name in filenames:
                (root / name).write_bytes(b"verified configuration\n")
            expected = evidence.configuration_snapshot(root)
            self.assertEqual(set(filenames), set(expected))
            (root / "obj").mkdir()
            (root / "obj" / "Tests.csproj.nuget.g.props").write_text("restore output")
            evidence.verify_configuration(root, expected)
            assets_path = root / "obj" / "project.assets.json"
            assets_path.write_bytes(b"verified packages")
            assets = {"obj/project.assets.json": evidence.sha256(assets_path)}
            evidence.verify_configuration(root, expected, assets)
            assets_path.write_bytes(b"changed packages")
            with self.assertRaisesRegex(AssertionError, "Migration configuration changed"):
                evidence.verify_configuration(root, expected, assets)
            for name in filenames:
                with self.subTest(file=name):
                    path = root / name
                    path.write_bytes(b"changed configuration\n")
                    with self.assertRaisesRegex(AssertionError, "Migration configuration changed"):
                        evidence.verify_configuration(root, expected)
                    path.unlink()
                    with self.assertRaisesRegex(AssertionError, "Migration configuration changed"):
                        evidence.verify_configuration(root, expected)
                    path.write_bytes(b"verified configuration\n")
            (root / "new.targets").write_text("new import")
            with self.assertRaisesRegex(AssertionError, "Migration configuration changed"):
                evidence.verify_configuration(root, expected)

    def test_workspace_preservation_includes_helpers_and_root_additions(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / ".eval").mkdir()
            for name in ("test.py", ".eval/guard.py"):
                (root / name).write_bytes(b"unchanged\n")
            expected = {name: evidence.sha256(root / name) for name in ("test.py", ".eval/guard.py")}
            (root / ".git").mkdir()
            (root / ".git" / "index").write_text("host state")
            (root / "__pycache__").mkdir()
            (root / "__pycache__" / "test.cpython-314.pyc").write_bytes(b"bytecode")
            evidence.preserve(root, expected, workspace=True)
            for name in ("unrequested.txt", "__pycache__/hidden.py"):
                (root / name).write_text("new file")
                with self.assertRaises(AssertionError):
                    evidence.preserve(root, expected, workspace=True)
                (root / name).unlink()
            (root / ".eval" / "guard.py").write_text("modified helper")
            with self.assertRaises(AssertionError):
                evidence.preserve(root, expected, workspace=True)

    def test_project_graph_binds_global_references_and_runtime_copies(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary).resolve()
            library, tests = root / "Library.csproj", root / "Tests.csproj"
            for project in (library, tests):
                project.write_text("<Project />")
            assembly = root / "lib" / "Library.dll"
            reference = root / "obj" / "ref" / "Library.dll"
            test_assembly = root / "test" / "Tests.dll"
            copied = test_assembly.parent / "Library.dll"
            for path in (assembly, reference, test_assembly, copied):
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(b"verified assembly")
            binding = {
                "ReferenceSourceTarget": "ProjectReference",
                "MSBuildSourceProjectFile": str(library),
                "FullPath": str(assembly), "ReferenceAssembly": str(reference),
            }
            original = {
                library: {"Properties": {"TargetPath": str(assembly), "TargetRefPath": str(reference)},
                          "Items": {}},
                tests: {"Properties": {"TargetPath": str(test_assembly), "TargetRefPath": str(test_assembly)},
                        "Items": {"ProjectReference": [{"FullPath": str(library)}],
                                  "ReferencePath": [binding],
                                  "CscCommandLineArgs": [{"Identity": "/reference:" + str(reference)}]}},
            }
            evidence.verify_project_graph(root, original, ["Library.csproj"])
            for mutation in ("missing-edge", "wrong-metadata", "alias-only", "runtime-copy"):
                with self.subTest(mutation=mutation):
                    builds = copy.deepcopy(original)
                    items = builds[tests]["Items"]
                    if mutation == "missing-edge":
                        items["ProjectReference"] = []
                    elif mutation == "wrong-metadata":
                        items["ReferencePath"][0]["ReferenceAssembly"] = str(assembly)
                    elif mutation == "alias-only":
                        items["CscCommandLineArgs"][0]["Identity"] = "/reference:unused=" + str(reference)
                    else:
                        copied.write_bytes(b"substituted assembly")
                    with self.assertRaisesRegex(AssertionError, "Project reference contract"):
                        evidence.verify_project_graph(root, builds, ["Library.csproj"])
                    copied.write_bytes(b"verified assembly")

    def compiler_fixture(self, directory):
        root = Path(directory) / "workspace"
        root.mkdir()
        project = root / "Tests.csproj"
        project.write_text('<Project Sdk="Microsoft.NET.Sdk" />')
        source = root / "Tests.cs"
        source.write_text("class Tests {}")
        generated = root / "obj" / "Debug" / "net8.0" / "Tests.AssemblyInfo.cs"
        generated.parent.mkdir(parents=True)
        generated.write_text("// generated assembly attributes")
        (root / "obj" / "project.assets.json").write_text('{"libraries": {}}')
        sdk = Path(directory) / "sdk"
        build = {
            "Properties": {"MSBuildToolsPath": str(sdk),
                           "NuGetPackageRoot": str(Path(directory) / "packages")},
            "Items": {
                "Compile": [
                    {"FullPath": str(source), "DefiningProjectFullPath": str(project)},
                    {"FullPath": str(generated), "DefiningProjectFullPath": str(
                        sdk / "Sdks" / "Microsoft.NET.Sdk" / "targets" /
                        "Microsoft.NET.GenerateAssemblyInfo.targets")},
                ],
                "CscCommandLineArgs": [
                    {"Identity": "/reference:Some.Reference.dll"},
                    {"Identity": str(source)}, {"Identity": str(generated)},
                    {"Identity": "/warnaserror+:NU1605"},
                ],
            },
        }
        return root, project, source, build

    def test_compiler_contract_accepts_aliases_and_sdk_generated_inputs(self):
        with tempfile.TemporaryDirectory() as directory:
            root, project, source, build = self.compiler_fixture(directory)
            contracts = evidence.compile_contracts(
                root, ["./Tests.csproj=./Tests.cs"], ["Tests.csproj"]
            )
            build["Items"]["CscCommandLineArgs"][1]["Identity"] = '"./Tests.cs"'
            actual = evidence.verify_compile_inputs(root, project, contracts[project.resolve()], build)
            self.assertEqual({"Tests.cs", "obj/Debug/net8.0/Tests.AssemblyInfo.cs"}, set(actual))
            self.assertEqual({project.resolve(): {source.resolve()}}, contracts)

    def test_compiler_contract_rejects_missing_and_uncontracted_inputs(self):
        with tempfile.TemporaryDirectory() as directory:
            root, project, source, original = self.compiler_fixture(directory)
            for mutation in ("missing", "replacement", "extra", "generated-metadata", "obj-file",
                             "forged-sdk-filename", "restored-after-compile"):
                build = copy.deepcopy(original)
                items = build["Items"]
                replacement = root / ("obj/Debug/net8.0/Replacement.cs"
                                      if mutation == "obj-file" else "Replacement.cs")
                replacement.write_text("class Replacement {}")
                with self.subTest(mutation=mutation):
                    if mutation in {"missing", "replacement", "restored-after-compile"}:
                        items["CscCommandLineArgs"].pop(1)
                        if mutation != "restored-after-compile":
                            items["Compile"].pop(0)
                    if mutation not in {"missing", "forged-sdk-filename"}:
                        items["CscCommandLineArgs"].insert(1, {"Identity": str(replacement)})
                        if mutation != "restored-after-compile":
                            items["Compile"].append({
                                "FullPath": str(replacement), "DefiningProjectFullPath": str(project),
                                "Generated": str(mutation == "generated-metadata"),
                                "AutoGen": str(mutation == "generated-metadata"),
                            })
                    if mutation == "forged-sdk-filename":
                        items["Compile"][1]["DefiningProjectFullPath"] = str(project)
                    with self.assertRaisesRegex(AssertionError, "Compiler input contract"):
                        evidence.verify_compile_inputs(root, project, {source}, build)

    def test_compiler_contract_accepts_only_exact_package_sources_and_provenance(self):
        with tempfile.TemporaryDirectory() as directory:
            root, project, source, build = self.compiler_fixture(directory)
            package = Path(directory) / "packages" / "microsoft.net.test.sdk" / "17.13.0"
            target = package / "build" / "netcoreapp3.1"
            target.mkdir(parents=True)
            program = target / "Microsoft.NET.Test.Sdk.Program.cs"
            program.write_text("class Program {}")
            (root / "obj" / "project.assets.json").write_text(json.dumps(
                {"libraries": {"Microsoft.NET.Test.Sdk/17.13.0": {}}}
            ))
            item = {"FullPath": str(program), "DefiningProjectFullPath": str(
                target / "Microsoft.NET.Test.Sdk.targets")}
            build["Items"]["Compile"].append(item)
            build["Items"]["CscCommandLineArgs"].append({"Identity": str(program)})
            evidence.verify_compile_inputs(root, project, {source}, build)
            item["DefiningProjectFullPath"] = str(project)
            with self.assertRaisesRegex(AssertionError, "Compiler input contract"):
                evidence.verify_compile_inputs(root, project, {source}, build)

    def test_compiler_contract_rejects_response_files_and_linked_sources(self):
        with tempfile.TemporaryDirectory() as directory:
            root, project, source, build = self.compiler_fixture(directory)
            build["Items"]["CscCommandLineArgs"].append({"Identity": "@sources.rsp"})
            with self.assertRaisesRegex(AssertionError, "response-file"):
                evidence.verify_compile_inputs(root, project, {source}, build)
            link = root / "Linked.cs"
            link.symlink_to(source)
            with self.assertRaisesRegex(AssertionError, "symlink or junction"):
                evidence.compile_contracts(root, ["Tests.csproj=Linked.cs"], ["Tests.csproj"])

    def test_compiler_contract_covers_every_project_and_stays_in_workspace(self):
        with tempfile.TemporaryDirectory() as directory:
            root, project, source, build = self.compiler_fixture(directory)
            for contracts, projects in (
                (["Tests.csproj=Tests.cs"], ["Tests.csproj", "Omitted.csproj"]),
                (["Tests.csproj=../outside.cs"], ["Tests.csproj"]),
                ([f"Tests.csproj={source}"], ["Tests.csproj"]),
            ):
                with self.subTest(contracts=contracts), self.assertRaisesRegex(
                    AssertionError, "Compiler input contract"
                ):
                    evidence.compile_contracts(root, contracts, projects)

    def test_compiler_paths_reject_linked_directories_before_canonicalizing(self):
        with tempfile.TemporaryDirectory() as directory:
            root, project, source, build = self.compiler_fixture(directory)
            link = root / "LinkedDirectory"
            if os.name == "nt":
                subprocess.run(
                    ["pwsh", "-NoProfile", "-NonInteractive", "-Command",
                     "New-Item -ItemType Junction -Path $env:COMPILE_LINK "
                     "-Target $env:COMPILE_TARGET | Out-Null"],
                    env=dict(os.environ, COMPILE_LINK=str(link), COMPILE_TARGET=str(root)),
                    check=True, capture_output=True, text=True, timeout=30,
                )
                self.assertTrue(link.is_junction())
            else:
                link.symlink_to(root, target_is_directory=True)
            try:
                for directory_arg, root_arg, value in (
                    (root, root, "LinkedDirectory/Tests.cs"),
                    (link, root, "Tests.cs"),
                    (link, link, "Tests.cs"),
                    (root.resolve(), root, str(link / "Tests.cs")),
                ):
                    with self.subTest(directory=directory_arg, root=root_arg, value=value):
                        with self.assertRaisesRegex(AssertionError, "symlink or junction"):
                            evidence.compiler_path(directory_arg, value, root_arg)
                with self.assertRaisesRegex(AssertionError, "symlink or junction"):
                    evidence.verify_compile_inputs(
                        root, link / project.name, {link / source.name}, build
                    )
            finally:
                if os.name == "nt":
                    link.rmdir()
                else:
                    link.unlink()

    def test_source_contract_normalizes_only_required_framework_rewrites(self):
        root = Path(__file__).resolve().parents[1]
        staged = (root / "agent.test-migration/fixtures/mstest-v2-vstest/OrderServiceTests.cs").read_text()
        expected = evidence.invariant_source(staged, "staged-orders")
        rewritten = staged.replace("ThrowsException", "ThrowsExactly").replace("[DataTestMethod]", "[TestMethod]")
        self.assertEqual(expected, evidence.invariant_source("// harmless comment\n" + rewritten, "staged-orders"))
        for before, after in (
            ('Assert.AreEqual("item-1", order.ItemId);', ""),
            ('"item-1"', '"item-2"'),
            ("quantity <= 0", "quantity < 0"),
        ):
            self.assertNotEqual(expected, evidence.invariant_source(rewritten.replace(before, after), "staged-orders"))
        upgrade = (root / "migrate-mstest-v3-to-v4/fixtures/v3-verified-migration/OrderServiceTests.cs").read_text()
        migrated = upgrade.replace("ClassCleanupBehavior.EndOfClass", "").replace("[ClassCleanup()]", "[ClassCleanup]").replace(
            "TestTimeout.Infinite", "int.MaxValue"
        )
        self.assertEqual(evidence.invariant_source(upgrade, "mstest-upgrade"),
                         evidence.invariant_source(migrated, "mstest-upgrade"))
        self.assertEqual(evidence.invariant_source(upgrade, "mstest-upgrade"),
                         evidence.invariant_source(migrated.replace("[ClassCleanup]", "[ClassCleanup()]"),
                                                   "mstest-upgrade"))
        self.assertNotEqual(evidence.invariant_source(upgrade, "mstest-upgrade"),
                            evidence.invariant_source(migrated.replace(
                                "Assert.AreEqual(42, orderId)", "Assert.AreEqual(orderId, orderId)"
                            ), "mstest-upgrade"))

    def test_checkpoint_rejects_external_and_dynamic_references(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            project = root / "Test.csproj"
            for item in (
                '<Compile Include="../outside.cs" />',
                '<ProjectReference Include="../Other/Other.csproj" />',
                '<Compile Include="C:\\outside\\file.cs" />',
                '<Import Project="../external.props" />',
                '<Compile Include="$(ExternalSource)" />',
            ):
                project.write_text(f'<Project Sdk="Microsoft.NET.Sdk"><ItemGroup>{item}</ItemGroup></Project>')
                with self.subTest(item=item), self.assertRaises(AssertionError):
                    evidence.validate_checkpoint(root)
            project.write_text('<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><Compile Include="src/Test.cs" /></ItemGroup></Project>')
            evidence.validate_checkpoint(root)

    def test_checkpoint_rejects_symlinked_sources(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary) / "checkpoint"
            root.mkdir()
            external = Path(temporary) / "External.cs"
            external.write_text("class External {}")
            (root / "Linked.cs").symlink_to(external)
            with self.assertRaisesRegex(AssertionError, "Checkpoint symlink"):
                evidence.validate_checkpoint(root)

    def test_checkpoint_and_preservation_reject_linked_directories(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary) / "checkpoint"
            root.mkdir()
            external = Path(temporary) / "final-project"
            external.mkdir()
            (external / "Test.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk" />')
            link = root / "OrderTests"
            if os.name == "nt":
                subprocess.run(
                    ["pwsh", "-NoProfile", "-NonInteractive", "-Command",
                     "New-Item -ItemType Junction -Path $env:CHECKPOINT_LINK "
                     "-Target $env:CHECKPOINT_TARGET | Out-Null"],
                    env=dict(os.environ, CHECKPOINT_LINK=str(link), CHECKPOINT_TARGET=str(external)),
                    check=True, capture_output=True, text=True, timeout=30,
                )
                self.assertFalse(link.is_symlink())
                self.assertTrue(link.is_junction())
            else:
                link.symlink_to(external, target_is_directory=True)
            try:
                for checkpoint in (root, link):
                    with self.subTest(root=checkpoint), self.assertRaisesRegex(
                        AssertionError, "Checkpoint symlink|Invalid checkpoint root"
                    ):
                        evidence.validate_checkpoint(checkpoint)
                expected = {"Test.csproj": evidence.sha256(external / "Test.csproj")}
                evidence.preserve(external, expected)
                for preserved, files in (
                    (link, expected),
                    (root, {"OrderTests/Test.csproj": expected["Test.csproj"]}),
                ):
                    with self.subTest(preserved=preserved), self.assertRaisesRegex(
                        AssertionError, "Symlink or junction in preserved scope"
                    ):
                        evidence.preserve(preserved, files)
            finally:
                if os.name == "nt":
                    link.rmdir()
                else:
                    link.unlink()
            self.assertTrue((external / "Test.csproj").is_file())

    def test_preservation_rejects_symlinked_root(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            external = root / "external"
            external.mkdir()
            source = external / "test.cs"
            source.write_bytes(b"original test\n")
            expected = {"test.cs": evidence.sha256(source)}
            evidence.preserve(external, expected)
            link = root / "preserved"
            link.symlink_to(external, target_is_directory=True)
            try:
                with self.assertRaisesRegex(AssertionError, "Symlink or junction in preserved scope"):
                    evidence.preserve(link, expected)
            finally:
                link.unlink()
            self.assertEqual(b"original test\n", source.read_bytes())

    def test_framework_and_adapter_are_checked_for_each_project(self):
        assets = {"MSTest.TestFramework/4.4.1": {}, "MSTest.TestAdapter/4.4.1": {}}
        evidence.verify_mstest_packages(assets, "Beta.Tests.csproj", "4", "4.4.1")
        for package in ("MSTest.TestFramework", "MSTest.TestAdapter"):
            changed = dict(assets)
            changed[package + "/4.5.1"] = changed.pop(package + "/4.4.1")
            with self.subTest(package=package), self.assertRaises(AssertionError):
                evidence.verify_mstest_packages(changed, "Beta.Tests.csproj", "4", "4.4.1")

    def test_equal_totals_do_not_hide_changed_case_set(self):
        with self.assertRaises(AssertionError):
            evidence.verify_cases(
                {"row-replacement": {"outcome": "Passed", "message": ""}},
                {"row-original": "Passed"}, {},
            )

    def test_skip_reason_is_not_just_a_skipped_count(self):
        with self.assertRaises(AssertionError):
            evidence.verify_cases(
                {"deferred": {"outcome": "NotExecuted", "message": "unrelated"}},
                {"deferred": "NotExecuted"}, {"deferred": "tracked reason"},
            )

    def test_missing_case_is_rejected(self):
        with self.assertRaises(AssertionError):
            evidence.verify_cases({}, {"required": "Passed"}, {})

    def test_known_complete_outcome_is_accepted(self):
        evidence.verify_cases(
            {"row": {"outcome": "Passed", "message": ""},
             "skip": {"outcome": "NotExecuted", "message": "tracked reason"}},
            {"row": "Passed", "skip": "NotExecuted"}, {"skip": "tracked reason"},
        )

    def test_preservation_covers_added_deleted_and_changed_siblings(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            first = root / "project.xml"
            second = root / "test.cs"
            first.write_bytes(b"version 2.2.10\n")
            second.write_bytes(b"original test\n")
            expected = {path.name: evidence.sha256(path) for path in (first, second)}
            evidence.preserve(root, expected)
            second.write_bytes(b"silently rewritten test\n")
            with self.assertRaises(AssertionError):
                evidence.preserve(root, expected)
            second.unlink()
            with self.assertRaises(AssertionError):
                evidence.preserve(root, expected)
            second.write_bytes(b"original test\n")
            (root / "new.cs").write_bytes(b"new file")
            with self.assertRaises(AssertionError):
                evidence.preserve(root, expected)

    def test_duplicate_id_and_inconsistent_counters_are_rejected(self):
        template = (
            '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
            '<Results>{results}</Results><ResultSummary><Counters total="{total}"/>'
            '</ResultSummary></TestRun>'
        )
        result = '<UnitTestResult testName="row" outcome="Passed"/>'
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "result.trx"
            for results, total in ((result * 2, 2), (result, 2)):
                path.write_text(template.format(results=results, total=total), encoding="utf-8")
                with self.assertRaises(AssertionError):
                    evidence.read_trx(path)


if __name__ == "__main__":
    unittest.main()
