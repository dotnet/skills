import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET

import yaml


SUITE = Path(__file__).resolve().parent
SPEC = yaml.safe_load((SUITE / "eval.yaml").read_text(encoding="utf-8"))


class GraderTests(unittest.TestCase):
    def workspace(self, capability):
        stimulus = next(s for s in SPEC["stimuli"] if s["tags"]["capability"] == capability)
        temporary = tempfile.TemporaryDirectory(prefix="repeated-work-grader-")
        self.addCleanup(temporary.cleanup)
        work = Path(temporary.name)
        for entry in stimulus["environment"]["files"]:
            source, target = SUITE / entry["src"], work / entry["dest"]
            if source.is_dir():
                shutil.copytree(source, target)
            else:
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(source, target)
        if "golden_patch" in stimulus:
            result = subprocess.run(
                ["git", "apply", "--whitespace=nowarn", str(SUITE / stimulus["golden_patch"]["path"])],
                cwd=work, capture_output=True, text=True,
            )
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        args = next(g["config"]["args"] for g in stimulus["graders"]
                    if g["type"] == "run-command")
        self.assertEqual(args[0], "-c")
        script = "import sys\nsys.argv = " + repr(["-c", *args[2:]]) + "\n" + args[1]
        return work, script

    def grade(self, work, script, *, accepted=True):
        env = dict(os.environ)
        additions = [str(Path(sys.executable).parent)]
        if os.name == "nt":
            additions.append(str(Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "Git" / "bin"))
        env["PATH"] = os.pathsep.join([*additions, env["PATH"]])
        result = subprocess.run(
            [sys.executable, "-c", script], cwd=work, env=env,
            capture_output=True, text=True, encoding="utf-8", timeout=240,
        )
        if accepted:
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        else:
            self.assertNotEqual(result.returncode, 0, "grader accepted the broken mutation")
        return result

    def test_all_golden_patches(self):
        for capability in (
            "invariant-per-project-exec", "global-property-instances",
            "racy-run-once-guard", "per-item-exec",
        ):
            with self.subTest(capability=capability):
                self.grade(*self.workspace(capability))

    def test_git_stamp_conditions_are_evaluated_by_msbuild(self):
        variants = (
            ("Target", "'$(Version)' == '3.2.0'"),
            ("Target/PropertyGroup", "'$(GitCommit)' != ''"),
            ("Target/PropertyGroup/InformationalVersion", "'$(GitCommit)' != ''"),
        )
        for selector, enabled in variants:
            for accepted, condition in ((True, enabled), (False, "'false' == 'true'")):
                with self.subTest(element=selector, accepted=accepted):
                    work, script = self.workspace("invariant-per-project-exec")
                    path = work / "git-stamp" / "Directory.Build.targets"
                    tree = ET.parse(path)
                    tree.getroot().find(selector).set("Condition", condition)
                    tree.write(path, encoding="utf-8")
                    result = self.grade(work, script, accepted=accepted)
                    if not accepted:
                        self.assertIn("the commit and version must be ready", result.stderr)

    def test_equivalent_git_fallback_conditions_are_accepted(self):
        for condition in ("('$(GitCommit)' == '')", "'' == '$(GitCommit)'"):
            with self.subTest(condition=condition):
                work, script = self.workspace("invariant-per-project-exec")
                path = work / "git-stamp" / "Directory.Build.targets"
                tree = ET.parse(path)
                tree.getroot().find("Target/Exec").set("Condition", condition)
                tree.write(path, encoding="utf-8")
                self.grade(work, script)

    def test_early_sdk_git_stamp_hooks_are_accepted(self):
        for hook in ("BeforeBuild", "PrepareForBuild"):
            with self.subTest(hook=hook):
                work, script = self.workspace("invariant-per-project-exec")
                path = work / "git-stamp" / "Directory.Build.targets"
                tree = ET.parse(path)
                tree.getroot().find("Target").set("BeforeTargets", hook)
                tree.write(path, encoding="utf-8")
                self.grade(work, script)

    def test_equivalent_git_stamp_rewrites_are_accepted(self):
        for variant in ("renamed-property", "property-function"):
            with self.subTest(variant=variant):
                work, script = self.workspace("invariant-per-project-exec")
                base = work / "git-stamp"
                if variant == "renamed-property":
                    for filename in ("Directory.Build.targets", "build.sh"):
                        path = base / filename
                        path.write_text(
                            path.read_text(encoding="utf-8").replace("GitCommit", "BuildRevision"),
                            encoding="utf-8",
                        )
                else:
                    path = base / "Directory.Build.targets"
                    tree = ET.parse(path)
                    tree.getroot().find("Target/PropertyGroup/InformationalVersion").text = (
                        "$([System.String]::Concat('$(Version)', '+', '$(GitCommit)'))"
                    )
                    tree.write(path, encoding="utf-8")
                self.grade(work, script)

    def test_git_stamp_orchestrator_is_evaluated_by_msbuild(self):
        for forwards_revision in (True, False):
            with self.subTest(forwards_revision=forwards_revision):
                work, script = self.workspace("invariant-per-project-exec")
                base = work / "git-stamp"
                (base / "build.sh").write_text(
                    "#!/usr/bin/env bash\nset -euo pipefail\n"
                    "dotnet msbuild Build.proj -p:Configuration=Release -m\n",
                    encoding="utf-8",
                )
                project = ET.Element("Project", DefaultTargets="BuildAll")
                target = ET.SubElement(project, "Target", Name="BuildAll")
                lookup = ET.SubElement(
                    target, "Exec", Command="git rev-parse --short HEAD", ConsoleToMSBuild="true",
                )
                ET.SubElement(lookup, "Output", TaskParameter="ConsoleOutput", PropertyName="Revision")
                ET.SubElement(
                    target, "Exec", Command="dotnet restore Contoso.slnx -p:Configuration=Release",
                )
                properties = "Configuration=Release"
                if forwards_revision:
                    properties += ";GitCommit=$(Revision)"
                ET.SubElement(
                    target, "MSBuild", Projects="Contoso.slnx", Targets="Build",
                    BuildInParallel="true", Properties=properties,
                )
                ET.ElementTree(project).write(base / "Build.proj", encoding="utf-8")
                result = self.grade(work, script, accepted=forwards_revision)
                if not forwards_revision:
                    self.assertIn("the fallback must run once", result.stderr)

    def test_missing_informational_version_attribute_is_rejected(self):
        work, script = self.workspace("invariant-per-project-exec")
        path = work / "git-stamp" / "Directory.Build.props"
        tree = ET.parse(path)
        ET.SubElement(
            tree.getroot().find("PropertyGroup"), "GenerateAssemblyInformationalVersionAttribute",
        ).text = "false"
        tree.write(path, encoding="utf-8")
        result = self.grade(work, script, accepted=False)
        self.assertIn("compiled informational-version attribute", result.stderr)

    def test_git_stamp_cannot_disable_parallel_builds(self):
        work, script = self.workspace("invariant-per-project-exec")
        path = work / "git-stamp" / "build.sh"
        path.write_text(
            path.read_text(encoding="utf-8").replace(
                "dotnet build Contoso.slnx", "dotnet build Contoso.slnx -p:BuildInParallel=false",
            ), encoding="utf-8",
        )
        result = self.grade(work, script, accepted=False)
        self.assertIn("parallel execution", result.stderr)

    def test_git_fallback_requires_the_property_to_be_absent(self):
        for condition in ("'true' == 'true'", "'false' == 'true'"):
            with self.subTest(condition=condition):
                work, script = self.workspace("invariant-per-project-exec")
                path = work / "git-stamp" / "Directory.Build.targets"
                tree = ET.parse(path)
                tree.getroot().find("Target/Exec").set("Condition", condition)
                tree.write(path, encoding="utf-8")
                result = self.grade(work, script, accepted=False)
                self.assertIn(
                    "the fallback must run once" if condition == "'true' == 'true'"
                    else "the commit and version must be ready",
                    result.stderr,
                )

    def test_late_git_stamp_assignment_is_rejected(self):
        work, script = self.workspace("invariant-per-project-exec")
        path = work / "git-stamp" / "Directory.Build.targets"
        tree = ET.parse(path)
        target = tree.getroot().find("Target")
        target.attrib.pop("BeforeTargets")
        target.set("AfterTargets", "GetAssemblyAttributes")
        tree.write(path, encoding="utf-8")
        result = self.grade(work, script, accepted=False)
        self.assertIn("the commit and version must be ready", result.stderr)

    def test_git_stamp_preserves_effective_build_arguments(self):
        variants = (
            (False, "", "'$(Configuration)' == 'Debug'"),
            (False, "", "'$(Configuration)' == 'Release'"),
            (False, "-p:StampMode=ci", "'$(StampMode)' != 'ci'"),
            (True, "-p:StampMode=ci", "'$(Configuration)' == 'Debug' or '$(StampMode)' == 'ci'"),
        )
        for accepted, argument, condition in variants:
            with self.subTest(argument=argument, condition=condition):
                work, script = self.workspace("invariant-per-project-exec")
                base = work / "git-stamp"
                if argument:
                    wrapper = base / "build.sh"
                    wrapper.write_text(
                        wrapper.read_text(encoding="utf-8").replace(
                            "dotnet build Contoso.slnx", f"dotnet build Contoso.slnx {argument}",
                        ), encoding="utf-8",
                    )
                path = base / "Directory.Build.targets"
                tree = ET.parse(path)
                tree.getroot().find("Target").set("Condition", condition)
                tree.write(path, encoding="utf-8")
                result = self.grade(work, script, accepted=accepted)
                if not accepted:
                    self.assertIn("the commit and version must be ready", result.stderr)

    def test_conditional_reference_updates_are_evaluated_by_msbuild(self):
        work, script = self.workspace("global-property-instances")
        base = work / "shared-project-instances"
        for component in ("Web", "Api", "Worker"):
            path = base / component / f"{component}.csproj"
            tree = ET.parse(path)
            group = tree.getroot().find("ItemGroup")
            group.set("Condition", "'$(Configuration)' == 'Debug'")
            reference = group.find("ProjectReference")
            reference.attrib.pop("GlobalPropertiesToRemove")
            reference.set("Condition", "Exists('../Core/Core.csproj')")
            tree.write(path, encoding="utf-8")
        (base / "Directory.Build.targets").write_text(
            """<Project>
  <ItemGroup Condition="'$(MSBuildProjectName)' != 'Core'">
    <ProjectReference Update="../Core/Core.csproj">
      <GlobalPropertiesToRemove Condition="'$(Configuration)' == 'Debug'">ComponentName</GlobalPropertiesToRemove>
    </ProjectReference>
  </ItemGroup>
</Project>
""", encoding="utf-8",
        )
        self.grade(work, script)

    def test_false_reference_removal_condition_is_rejected(self):
        work, script = self.workspace("global-property-instances")
        for path in (work / "shared-project-instances").glob("*/*.csproj"):
            tree = ET.parse(path)
            for reference in tree.getroot().iter("ProjectReference"):
                reference.attrib.pop("GlobalPropertiesToRemove")
                ET.SubElement(
                    reference, "GlobalPropertiesToRemove",
                    Condition="'$(Configuration)' == 'Release'",
                ).text = "ComponentName"
            tree.write(path, encoding="utf-8")
        result = self.grade(work, script, accepted=False)
        self.assertIn("build Core exactly once", result.stderr)

    def test_missing_compiler_reference_is_rejected(self):
        work, script = self.workspace("global-property-instances")
        path = work / "shared-project-instances" / "Api" / "Api.csproj"
        tree = ET.parse(path)
        tree.getroot().find("ItemGroup/ProjectReference").set("ReferenceOutputAssembly", "false")
        tree.write(path, encoding="utf-8")
        result = self.grade(work, script, accepted=False)
        self.assertIn("actual compilation reference", result.stderr)

    def test_missing_component_title_attributes_are_rejected(self):
        for component, flag in (
            ("Web", "GenerateAssemblyTitleAttribute"),
            ("Api", "GenerateAssemblyInfo"),
        ):
            with self.subTest(component=component, flag=flag):
                work, script = self.workspace("global-property-instances")
                path = work / "shared-project-instances" / component / f"{component}.csproj"
                tree = ET.parse(path)
                ET.SubElement(tree.getroot().find("PropertyGroup"), flag).text = "false"
                tree.write(path, encoding="utf-8")
                result = self.grade(work, script, accepted=False)
                self.assertIn("compiled component AssemblyTitle", result.stderr)

    def test_csharp_only_compile_include_is_accepted(self):
        work, script = self.workspace("racy-run-once-guard")
        path = work / "exists-sentinel-race" / "Directory.Build.targets"
        tree = ET.parse(path)
        tree.getroot().find("ItemGroup").set(
            "Condition", "'$(MSBuildProjectExtension)' == '.csproj'",
        )
        tree.write(path, encoding="utf-8")
        self.grade(work, script)

    def test_wrong_project_extension_compile_include_is_rejected(self):
        work, script = self.workspace("racy-run-once-guard")
        path = work / "exists-sentinel-race" / "Directory.Build.targets"
        tree = ET.parse(path)
        tree.getroot().find("ItemGroup").set(
            "Condition", "'$(MSBuildProjectExtension)' == '.vbproj'",
        )
        tree.write(path, encoding="utf-8")
        result = self.grade(work, script, accepted=False)
        self.assertIn("effective Compile items", result.stderr)

    def test_shared_generation_orchestrator_is_accepted(self):
        for task_dispatch in (False, True):
            with self.subTest(msbuild_task=task_dispatch):
                work, script = self.workspace("racy-run-once-guard")
                base = work / "exists-sentinel-race"
                wrapper = base / "build.sh"
                lines = wrapper.read_text(encoding="utf-8").splitlines()
                dispatch = lines.pop()
                self.assertTrue(dispatch.startswith("dotnet build Contoso.slnx"))
                (base / "prepare.sh").write_text("\n".join(lines) + "\n", encoding="utf-8")
                wrapper.write_text(
                    "#!/usr/bin/env bash\nset -euo pipefail\n"
                    "dotnet msbuild Build.proj -p:Configuration=Release -m\n",
                    encoding="utf-8",
                )
                project = ET.Element("Project", DefaultTargets="BuildAll")
                target = ET.SubElement(project, "Target", Name="BuildAll")
                ET.SubElement(target, "Exec", Command="bash prepare.sh")
                if task_dispatch:
                    ET.SubElement(target, "Exec", Command="dotnet restore Contoso.slnx -p:Configuration=Release")
                    ET.SubElement(
                        target, "MSBuild", Projects="Contoso.slnx", Targets="Build",
                        BuildInParallel="true", Properties="Configuration=Release",
                    )
                else:
                    ET.SubElement(target, "Exec", Command=dispatch)
                ET.ElementTree(project).write(base / "Build.proj", encoding="utf-8")
                self.grade(work, script)

    def test_late_shared_generation_is_rejected(self):
        work, script = self.workspace("racy-run-once-guard")
        base = work / "exists-sentinel-race"
        wrapper = base / "build.sh"
        lines = wrapper.read_text(encoding="utf-8").splitlines()
        dispatch = lines.pop()
        (base / "prepare.sh").write_text("\n".join(lines) + "\n", encoding="utf-8")
        wrapper.write_text("#!/usr/bin/env bash\nset -euo pipefail\n" + dispatch + "\n", encoding="utf-8")
        path = base / "Directory.Build.targets"
        tree = ET.parse(path)
        target = ET.SubElement(tree.getroot(), "Target", Name="LateGeneration", AfterTargets="CoreCompile")
        ET.SubElement(
            target, "Exec", Command='bash "$(MSBuildThisFileDirectory)prepare.sh"',
            WorkingDirectory="$(MSBuildThisFileDirectory)",
        )
        tree.write(path, encoding="utf-8")
        self.grade(work, script, accepted=False)

    def test_repeated_shared_generation_is_rejected(self):
        work, script = self.workspace("racy-run-once-guard")
        path = work / "exists-sentinel-race" / "Directory.Build.targets"
        tree = ET.parse(path)
        target = ET.SubElement(tree.getroot(), "Target", Name="RepeatedGeneration", BeforeTargets="CoreCompile")
        ET.SubElement(
            target, "Exec", Command="sh tools/make-buildinfo.sh obj/buildinfo.g.cs",
            WorkingDirectory="$(MSBuildThisFileDirectory)",
        )
        tree.write(path, encoding="utf-8")
        self.grade(work, script, accepted=False)

    def test_global_property_keeps_shared_fallback_dormant(self):
        work, script = self.workspace("racy-run-once-guard")
        base = work / "exists-sentinel-race"
        wrapper = base / "build.sh"
        wrapper.write_text(
            wrapper.read_text(encoding="utf-8").replace(
                "dotnet build Contoso.slnx", "dotnet build Contoso.slnx -p:SharedInfoReady=true",
            ), encoding="utf-8",
        )
        path = base / "Directory.Build.targets"
        tree = ET.parse(path)
        target = ET.SubElement(
            tree.getroot(), "Target", Name="GuardedGeneration", BeforeTargets="CoreCompile",
            Condition="'$(SharedInfoReady)' != 'true'",
        )
        ET.SubElement(
            target, "Exec", Command="sh tools/make-buildinfo.sh obj/buildinfo.g.cs",
            WorkingDirectory="$(MSBuildThisFileDirectory)",
        )
        tree.write(path, encoding="utf-8")
        self.grade(work, script)

    def test_forced_recompilation_on_unchanged_builds_is_rejected(self):
        work, script = self.workspace("racy-run-once-guard")
        path = work / "exists-sentinel-race" / "Directory.Build.targets"
        tree = ET.parse(path)
        target = ET.SubElement(
            tree.getroot(), "Target", Name="ForceRecompile", BeforeTargets="CoreCompile",
        )
        ET.SubElement(target, "Delete", Files="@(IntermediateAssembly)")
        tree.write(path, encoding="utf-8")
        result = self.grade(work, script, accepted=False)
        self.assertIn("unchanged", result.stderr)

    def test_cross_schema_copy_and_swap_are_rejected(self):
        for swap in (False, True):
            with self.subTest(swap=swap):
                work, script = self.workspace("per-item-exec")
                path = work / "per-file-exec" / "Schemas.targets"
                tree = ET.parse(path)
                target = tree.getroot().find("Target")
                customer = "$(IntermediateOutputPath)gen/Customer.g.cs"
                order = "$(IntermediateOutputPath)gen/Order.g.cs"
                if swap:
                    saved = "$(IntermediateOutputPath)gen/saved.txt"
                    ET.SubElement(target, "Copy", SourceFiles=customer, DestinationFiles=saved)
                    ET.SubElement(target, "Copy", SourceFiles=order, DestinationFiles=customer)
                    ET.SubElement(target, "Copy", SourceFiles=saved, DestinationFiles=order)
                else:
                    ET.SubElement(target, "Copy", SourceFiles=customer, DestinationFiles=order)
                tree.write(path, encoding="utf-8")
                result = self.grade(work, script, accepted=False)
                self.assertIn("distinct content-dependent schema output", result.stderr)

    def test_corruption_of_each_schema_is_rejected(self):
        for name in ("Customer", "Invoice", "Order"):
            with self.subTest(schema=name):
                work, script = self.workspace("per-item-exec")
                path = work / "per-file-exec" / "Schemas.targets"
                tree = ET.parse(path)
                ET.SubElement(
                    tree.getroot().find("Target"), "WriteLinesToFile",
                    File=f"$(IntermediateOutputPath)gen/{name}.g.cs",
                    Lines="// corrupt", Overwrite="true",
                )
                tree.write(path, encoding="utf-8")
                self.grade(work, script, accepted=False)

    def test_schema_expectations_precede_submitted_target_execution(self):
        work, script = self.workspace("per-item-exec")
        path = work / "per-file-exec" / "Schemas.targets"
        tree = ET.parse(path)
        tree.getroot().find("Target").insert(0, ET.Element(
            "WriteLinesToFile", File="schemas/Order.json", Lines="{}", Overwrite="true",
        ))
        tree.write(path, encoding="utf-8")
        result = self.grade(work, script, accepted=False)
        self.assertIn("must not modify, add or delete schema inputs", result.stderr)

    def test_enabled_schema_exec_condition_is_accepted(self):
        work, script = self.workspace("per-item-exec")
        path = work / "per-file-exec" / "Schemas.targets"
        tree = ET.parse(path)
        tree.getroot().find("Target/Exec").set("Condition", "Exists('schemas/Customer.json')")
        tree.write(path, encoding="utf-8")
        self.grade(work, script)

    def test_disabled_schema_exec_condition_is_rejected(self):
        work, script = self.workspace("per-item-exec")
        path = work / "per-file-exec" / "Schemas.targets"
        tree = ET.parse(path)
        tree.getroot().find("Target/Exec").set("Condition", "Exists('schemas/Missing.json')")
        tree.write(path, encoding="utf-8")
        result = self.grade(work, script, accepted=False)
        self.assertIn("clean: schema generation must execute exactly once", result.stderr)

    def test_equivalent_schema_item_name_is_accepted(self):
        work, script = self.workspace("per-item-exec")
        path = work / "per-file-exec" / "Schemas.targets"
        path.write_text(
            path.read_text(encoding="utf-8").replace("SchemaFile", "SchemaInput"),
            encoding="utf-8",
        )
        self.grade(work, script)

    def test_imported_schema_implementation_preserves_original_files(self):
        for variant in ("preserved", "modified-evidence", "deleted-evidence"):
            with self.subTest(variant=variant):
                work, script = self.workspace("per-item-exec")
                path = work / "per-file-exec" / "Schemas.targets"
                path.with_name("BatchSchemas.targets").write_bytes(path.read_bytes())
                path.write_text('<Project><Import Project="BatchSchemas.targets" /></Project>', encoding="utf-8")
                if variant == "modified-evidence":
                    evidence = path.parent / "perf-summary.txt"
                    evidence.write_bytes(evidence.read_bytes() + b"\nChanged evidence\n")
                elif variant == "deleted-evidence":
                    (path.parent / "tools" / "schemagen.md").unlink()
                result = self.grade(work, script, accepted=variant == "preserved")
                if variant != "preserved":
                    self.assertIn("preserve all original schema contents and evidence files", result.stderr)

    def test_property_backed_schema_generator_is_accepted(self):
        work, script = self.workspace("per-item-exec")
        path = work / "per-file-exec" / "Schemas.targets"
        tree = ET.parse(path)
        root = tree.getroot()
        properties = ET.Element("PropertyGroup")
        ET.SubElement(properties, "GeneratorExe").text = "schemagen"
        root.insert(0, properties)
        task = root.find("Target/Exec")
        task.set("Command", task.get("Command").replace("schemagen", "$(GeneratorExe)"))
        tree.write(path, encoding="utf-8")
        self.grade(work, script)

    def test_anchored_schema_output_directory_is_accepted(self):
        work, script = self.workspace("per-item-exec")
        path = work / "per-file-exec" / "Schemas.targets"
        tree = ET.parse(path)
        task = tree.getroot().find("Target/Exec")
        task.set(
            "Command",
            task.get("Command").replace(
                '--output-dir "$(IntermediateOutputPath)gen"',
                '--output-dir "$(MSBuildProjectDirectory)/$(IntermediateOutputPath)gen"',
            ),
        )
        tree.write(path, encoding="utf-8")
        self.grade(work, script)

    def test_nonincremental_schema_targets_are_rejected(self):
        for variant in ("no-tracking", "missing-output"):
            with self.subTest(variant=variant):
                work, script = self.workspace("per-item-exec")
                path = work / "per-file-exec" / "Schemas.targets"
                tree = ET.parse(path)
                target = tree.getroot().find("Target")
                if variant == "no-tracking":
                    target.attrib.pop("Inputs")
                    target.attrib.pop("Outputs")
                else:
                    target.set("Outputs", "$(IntermediateOutputPath)always-missing.output")
                tree.write(path, encoding="utf-8")
                result = self.grade(work, script, accepted=False)
                self.assertIn("unchanged: schema generation", result.stderr)

    def test_schema_compile_hooks_are_evaluated_by_msbuild(self):
        for hook in ("property-before", "early-sdk", "property-dependency"):
            with self.subTest(hook=hook):
                work, script = self.workspace("per-item-exec")
                path = work / "per-file-exec" / "Schemas.targets"
                tree = ET.parse(path)
                root = tree.getroot()
                target = root.find("Target")
                if hook == "property-before":
                    properties = ET.Element("PropertyGroup")
                    ET.SubElement(properties, "SchemaCompileHook").text = "CoreCompile"
                    root.insert(0, properties)
                    target.set("BeforeTargets", "$(SchemaCompileHook)")
                elif hook == "early-sdk":
                    target.set("BeforeTargets", "PrepareForBuild")
                else:
                    target.attrib.pop("BeforeTargets")
                    properties = ET.Element("PropertyGroup")
                    ET.SubElement(properties, "SchemaCompileDependencies").text = target.get("Name")
                    root.insert(0, properties)
                    ET.SubElement(
                        root, "Target", Name="PrepareSchemas", BeforeTargets="CoreCompile",
                        DependsOnTargets="$(SchemaCompileDependencies)",
                    )
                tree.write(path, encoding="utf-8")
                self.grade(work, script)

    def test_unreachable_and_late_schema_generation_are_rejected(self):
        for hook in ("unreachable", "after-compilation"):
            with self.subTest(hook=hook):
                work, script = self.workspace("per-item-exec")
                path = work / "per-file-exec" / "Schemas.targets"
                tree = ET.parse(path)
                target = tree.getroot().find("Target")
                if hook == "unreachable":
                    target.set("BeforeTargets", "_MissingCompileHook")
                else:
                    target.attrib.pop("BeforeTargets")
                    target.set("AfterTargets", "CoreCompile")
                tree.write(path, encoding="utf-8")
                result = self.grade(work, script, accepted=False)
                self.assertIn("clean:", result.stderr)

    def test_anchored_schema_import_is_accepted(self):
        work, script = self.workspace("per-item-exec")
        path = work / "per-file-exec" / "Contoso.Schemas.csproj"
        tree = ET.parse(path)
        tree.getroot().find("Import").set("Project", "$(MSBuildThisFileDirectory)Schemas.targets")
        tree.write(path, encoding="utf-8")
        self.grade(work, script)

    def test_schema_conditions_are_evaluated_by_msbuild(self):
        variants = (
            ("import", "Import", "Exists('Schemas.targets')", "Exists('Missing.targets')"),
            ("target", "Target", "'$(TargetFramework)' == 'net10.0'", "'$(TargetFramework)' == 'net9.0'"),
            ("writer", "Target/WriteLinesToFile", "'@(SchemaFile)' != ''", "'@(SchemaFile)' == ''"),
            ("directory", "Target/MakeDir", "!Exists('$(IntermediateOutputPath)gen')", "'false' == 'true'"),
        )
        for kind, selector, enabled, disabled in variants:
            for accepted, condition in ((True, enabled), (False, disabled)):
                with self.subTest(operation=kind, accepted=accepted):
                    work, script = self.workspace("per-item-exec")
                    path = work / "per-file-exec" / (
                        "Contoso.Schemas.csproj" if kind == "import" else "Schemas.targets"
                    )
                    tree = ET.parse(path)
                    tree.getroot().find(selector).set("Condition", condition)
                    if kind == "directory" and not accepted:
                        # The list writer creates its parent, so keep it outside the missing output directory.
                        target = tree.getroot().find("Target")
                        writer = target.find("WriteLinesToFile")
                        old_list = writer.get("File")
                        new_list = "$(IntermediateOutputPath)schemas.list"
                        writer.set("File", new_list)
                        task = target.find("Exec")
                        self.assertIn(old_list, task.get("Command"))
                        task.set("Command", task.get("Command").replace(old_list, new_list))
                    tree.write(path, encoding="utf-8")
                    result = self.grade(work, script, accepted=accepted)
                    if not accepted:
                        self.assertIn("clean:", result.stderr)

    def test_redundant_disabled_schema_makedir_is_accepted(self):
        work, script = self.workspace("per-item-exec")
        path = work / "per-file-exec" / "Schemas.targets"
        tree = ET.parse(path)
        tree.getroot().find("Target/MakeDir").set("Condition", "'false' == 'true'")
        tree.write(path, encoding="utf-8")
        self.grade(work, script)

    def test_schema_list_writer_can_create_output_directory(self):
        work, script = self.workspace("per-item-exec")
        path = work / "per-file-exec" / "Schemas.targets"
        tree = ET.parse(path)
        target = tree.getroot().find("Target")
        target.remove(target.find("MakeDir"))
        tree.write(path, encoding="utf-8")
        self.grade(work, script)

    def test_after_compilation_schema_cleanup_is_rejected(self):
        for phase in ("CoreCompile", "Build"):
            with self.subTest(after=phase):
                work, script = self.workspace("per-item-exec")
                path = work / "per-file-exec" / "Schemas.targets"
                tree = ET.parse(path)
                target = ET.SubElement(tree.getroot(), "Target", Name="DeleteGenerated", AfterTargets=phase)
                ET.SubElement(target, "RemoveDir", Directories="$(IntermediateOutputPath)gen")
                tree.write(path, encoding="utf-8")
                result = self.grade(work, script, accepted=False)
                self.assertIn("clean:", result.stderr)

    def test_generated_schemas_must_be_compile_inputs_at_compilation(self):
        work, script = self.workspace("per-item-exec")
        path = work / "per-file-exec" / "Schemas.targets"
        tree = ET.parse(path)
        for phase, operation in (("BeforeTargets", "Remove"), ("AfterTargets", "Include")):
            target = ET.SubElement(tree.getroot(), "Target", Name=operation + "Compile", **{phase: "CoreCompile"})
            group = ET.SubElement(target, "ItemGroup")
            ET.SubElement(group, "Compile", **{operation: "$(IntermediateOutputPath)gen/*.g.cs"})
        tree.write(path, encoding="utf-8")
        result = self.grade(work, script, accepted=False)
        self.assertIn("Compile input at compilation", result.stderr)

    def test_generated_directory_cleanup_is_rejected(self):
        for inside in (False, True):
            with self.subTest(inside_incremental_target=inside):
                work, script = self.workspace("per-item-exec")
                path = work / "per-file-exec" / "Schemas.targets"
                tree = ET.parse(path)
                generator = tree.getroot().find("Target")
                cleanup = ET.Element("RemoveDir", Directories="$(IntermediateOutputPath)gen")
                if inside:
                    generator.insert(0, cleanup)
                else:
                    ET.SubElement(
                        tree.getroot(), "Target", Name="_DeleteGenerated",
                        BeforeTargets=generator.get("Name"),
                    ).append(cleanup)
                tree.write(path, encoding="utf-8")
                result = self.grade(work, script, accepted=False)
                self.assertIn("single-stale:" if inside else "unchanged:", result.stderr)

    def test_schema_regeneration_is_limited_to_stale_inputs(self):
        work, script = self.workspace("per-item-exec")
        path = work / "per-file-exec" / "Schemas.targets"
        tree = ET.parse(path)
        group = ET.Element("ItemGroup")
        ET.SubElement(group, "SchemaFile", Remove="@(SchemaFile)")
        ET.SubElement(group, "SchemaFile", Include="schemas/**/*.json")
        tree.getroot().find("Target").insert(0, group)
        tree.write(path, encoding="utf-8")
        result = self.grade(work, script, accepted=False)
        self.assertIn("single-stale: generate only the stale schemas", result.stderr)

    def test_unchanged_schema_timestamps_are_preserved(self):
        work, script = self.workspace("per-item-exec")
        path = work / "per-file-exec" / "Schemas.targets"
        tree = ET.parse(path)
        after = ET.SubElement(
            tree.getroot(), "Target", Name="_RewriteCurrentOutput",
            AfterTargets=tree.getroot().find("Target").get("Name"),
        )
        ET.SubElement(
            after, "Touch", Files="$(IntermediateOutputPath)gen/Customer.g.cs", ForceTouch="true",
        )
        tree.write(path, encoding="utf-8")
        result = self.grade(work, script, accepted=False)
        self.assertIn("unchanged: preserve every generated output timestamp", result.stderr)

    def test_single_stale_generation_preserves_current_timestamps(self):
        work, script = self.workspace("per-item-exec")
        path = work / "per-file-exec" / "Schemas.targets"
        tree = ET.parse(path)
        ET.SubElement(
            tree.getroot().find("Target"), "Touch",
            Files="$(IntermediateOutputPath)gen/Customer.g.cs", ForceTouch="true",
        )
        tree.write(path, encoding="utf-8")
        result = self.grade(work, script, accepted=False)
        self.assertIn("single-stale: preserve current outputs and their timestamps", result.stderr)

    def test_suite_contract_and_inline_graders(self):
        guards = [s for s in SPEC["stimuli"] if s.get("tags", {}).get("expect_activation") == "false"]
        self.assertEqual(len(guards), 7)
        self.assertEqual(len(SPEC["stimuli"]) - len(guards), 23)
        self.assertEqual(SPEC["defaults"]["timeout"], "6m")
        for stimulus in SPEC["stimuli"]:
            with self.subTest(stimulus=stimulus["name"]):
                for grader in stimulus["graders"]:
                    if grader["type"] == "run-command":
                        compile(grader["config"]["args"][1], stimulus["name"], "exec")
                    elif grader["type"] == "output-matches":
                        steps = stimulus["golden_trajectory"]["inline"]["steps"]
                        response = next(s["message"] for s in reversed(steps) if s["source"] == "agent")
                        self.assertRegex(response, re.compile(grader["config"]["pattern"]))

    def test_audit_goldens_and_complete_evidence_preservation(self):
        audits = [s for s in SPEC["stimuli"]
                  if any(f["dest"] == "audit/evidence.txt"
                         for f in s.get("environment", {}).get("files", []))]
        self.assertEqual(len(audits), 16)
        for stimulus in audits:
            with self.subTest(stimulus=stimulus["name"]):
                work, script = self.workspace(stimulus["tags"]["capability"])
                self.grade(work, script)
                extra = work / "audit" / "extra.txt"
                extra.write_text("unexpected file", encoding="utf-8")
                self.grade(work, script, accepted=False)
                extra.unlink()
                evidence = work / "audit" / "evidence.txt"
                evidence.write_bytes(evidence.read_bytes()[:20])
                self.grade(work, script, accepted=False)
                evidence.unlink()
                self.grade(work, script, accepted=False)


if __name__ == "__main__":
    unittest.main()
