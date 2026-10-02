#!/usr/bin/env python3
"""Integration tests for the installable Unskip Closed Tests package."""

from __future__ import annotations

import importlib.util
import tempfile
import unittest
from pathlib import Path


SCRIPT_DIR = Path(__file__).resolve().parent
REPO_ROOT = SCRIPT_DIR.parents[1]


def load_module(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    assert spec and spec.loader
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


VALIDATOR = load_module(
    "validate_agentic_workflows",
    SCRIPT_DIR / "validate_agentic_workflows.py",
)
STAGER = load_module(
    "stage_agentic_workflow_package",
    SCRIPT_DIR / "stage_agentic_workflow_package.py",
)


class UnskipClosedTestsPackageTests(unittest.TestCase):
    def test_write_credentials_are_configured_only_for_publication(self) -> None:
        prepare = (
            REPO_ROOT
            / "agentic-workflows"
            / "unskip-closed-tests"
            / "workflows"
            / "unskip-closed-tests-prepare.md"
        ).read_text(encoding="utf-8")

        self.assertNotIn("persist-credentials: true", prepare)
        publish_start = prepare.index("- name: Publish one verified draft pull request")
        auth_setup = prepare.index("gh auth setup-git", publish_start)
        first_push = prepare.index('git push origin "HEAD:refs/heads/$BRANCH"', publish_start)
        self.assertLess(auth_setup, first_push)

    def test_manifest_stages_buildable_tool_and_strict_workflow(self) -> None:
        package = REPO_ROOT / "agentic-workflows" / "unskip-closed-tests"
        manifest = package / "aw.yml"
        tool = package / "workflows" / "unskip-closed-tests-tool"
        includes = set(VALIDATOR.manifest_includes(manifest))
        required_tool_files = {
            path.relative_to(package).as_posix()
            for path in tool.rglob("*")
            if path.is_file() and "bin" not in path.parts and "obj" not in path.parts
        }
        missing = sorted(required_tool_files - includes)
        self.assertEqual(
            missing,
            [],
            f"aw.yml omits staged tool files: {', '.join(missing)}",
        )
        encoding_files = list(tool.glob("*.cs")) + [
            tool / "UnskipClosedTests.Tool.csproj"
        ]
        for source in encoding_files:
            self.assertTrue(
                source.read_bytes().startswith(b"\xef\xbb\xbf"),
                f"{source.relative_to(REPO_ROOT)} must be UTF-8 with BOM",
            )

        with tempfile.TemporaryDirectory(prefix="unskip-package-") as temp_dir:
            consumer = Path(temp_dir)
            (consumer / "global.json").write_text(
                """{
  "sdk": {
    "version": "99.0.100-impossible"
  }
}
""",
                encoding="utf-8",
            )
            (consumer / "Directory.Build.props").write_text(
                """<Project>
  <Target Name="RejectUnisolatedToolProps" BeforeTargets="PrepareForBuild">
    <Error Text="The staged tool imported consumer Directory.Build.props." />
  </Target>
</Project>
""",
                encoding="utf-8",
            )
            (consumer / "Directory.Build.targets").write_text(
                """<Project>
  <Target Name="RejectUnisolatedToolTargets" BeforeTargets="CoreCompile">
    <Error Text="The staged tool imported consumer Directory.Build.targets." />
  </Target>
</Project>
""",
                encoding="utf-8",
            )
            (consumer / "Directory.Packages.props").write_text(
                """<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
</Project>
""",
                encoding="utf-8",
            )
            staged = STAGER.stage_package(manifest, consumer)
            for include in required_tool_files:
                destination = VALIDATOR.package_destination(include)
                self.assertIn(destination, staged)
                self.assertEqual(
                    (consumer / destination).read_bytes(),
                    (package / include).read_bytes(),
                )

            VALIDATOR.run(["git", "init", "--quiet"], consumer)
            project = (
                consumer
                / ".github"
                / "workflows"
                / "unskip-closed-tests-tool"
                / "UnskipClosedTests.Tool.csproj"
            )
            VALIDATOR.run(
                ["dotnet", "restore", project.name, "--locked-mode"],
                project.parent,
            )
            VALIDATOR.run(
                [
                    "dotnet",
                    "build",
                    project.name,
                    "--no-restore",
                    "--configuration",
                    "Release",
                ],
                project.parent,
            )
            VALIDATOR.run(
                [
                    "gh",
                    "aw",
                    "compile",
                    "unskip-closed-tests",
                    "--strict",
                    "--validate",
                    "--schedule-seed",
                    "dotnet/skills",
                    "--action-mode",
                    "action",
                    "--action-tag",
                    VALIDATOR.GH_AW_ACTIONS_SHA,
                    "--json",
                ],
                consumer,
            )


if __name__ == "__main__":
    unittest.main()
