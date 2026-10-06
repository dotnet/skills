#!/usr/bin/env python3
"""Tests for the agentic workflow validation helpers."""

from __future__ import annotations

import importlib.util
import json
import tempfile
import unittest
from pathlib import Path
from unittest import mock


MODULE_PATH = Path(__file__).with_name("validate_agentic_workflows.py")
SPEC = importlib.util.spec_from_file_location("validate_agentic_workflows", MODULE_PATH)
assert SPEC and SPEC.loader
VALIDATOR = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(VALIDATOR)

STAGER_MODULE_PATH = Path(__file__).with_name("stage_agentic_workflow_package.py")
STAGER_SPEC = importlib.util.spec_from_file_location(
    "stage_agentic_workflow_package", STAGER_MODULE_PATH
)
assert STAGER_SPEC and STAGER_SPEC.loader
STAGER = importlib.util.module_from_spec(STAGER_SPEC)
STAGER_SPEC.loader.exec_module(STAGER)


class PackagePathTests(unittest.TestCase):
    def test_resolves_package_file_inside_staging_root(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            package = root / "package"
            scratch = root / "scratch"
            source = package / "workflows" / "example.md"
            source.parent.mkdir(parents=True)
            source.write_text("---\non: workflow_dispatch\n---\n", encoding="utf-8")
            scratch.mkdir()

            actual_source, destination = VALIDATOR.resolve_package_include(
                package / "aw.yml", "workflows/example.md", scratch
            )

            self.assertEqual(actual_source, source.resolve())
            self.assertEqual(
                destination,
                (scratch / ".github" / "workflows" / "example.md").resolve(),
            )

    def test_rejects_include_that_escapes_package(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            package = root / "package"
            package.mkdir()

            with self.assertRaisesRegex(RuntimeError, "escapes its package directory"):
                VALIDATOR.resolve_package_include(
                    package / "aw.yml", "../outside.md", root / "scratch"
                )

    def test_rejects_absolute_include(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            manifest = root / "package" / "aw.yml"

            with self.assertRaisesRegex(RuntimeError, "absolute include path"):
                VALIDATOR.resolve_package_include(
                    manifest, str((root / "outside.md").resolve()), root / "scratch"
                )

    def test_discovers_automatic_grader_package_resource(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            workflow = Path(temp_dir) / "workflows" / "example.md"
            workflow.parent.mkdir(parents=True)
            workflow.write_text(
                """---
on: workflow_dispatch
graders:
  operational-value:
    run: .github/graders/example-operational-value.sh
---
""",
                encoding="utf-8",
            )

            self.assertEqual(
                VALIDATOR.grader_evaluator_paths(workflow),
                [".github/graders/example-operational-value.sh"],
            )

    def test_stages_automatic_grader_resource_in_installed_layout(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            repo_root = Path(temp_dir)
            package = repo_root / "agentic-workflows" / "example"
            workflow = package / "workflows" / "example.md"
            evaluator = repo_root / ".github" / "graders" / "example-operational-value.sh"
            workflow.parent.mkdir(parents=True)
            evaluator.parent.mkdir(parents=True)
            (package / "aw.yml").write_text(
                """includes:
  - workflows/example.md
""",
                encoding="utf-8",
            )
            workflow.write_text(
                """---
on: workflow_dispatch
graders:
  operational-value:
    run: .github/graders/example-operational-value.sh
---
""",
                encoding="utf-8",
            )
            evaluator.write_text("#!/usr/bin/env bash\nprintf '[]\\n'\n", encoding="utf-8")

            def verify_staging(command: list[str], cwd: Path) -> None:
                if command[:3] != ["gh", "aw", "compile"]:
                    return
                self.assertEqual(
                    (cwd / ".github" / "graders" / evaluator.name).read_bytes(),
                    evaluator.read_bytes(),
                )

            with mock.patch.object(VALIDATOR, "run", side_effect=verify_staging):
                VALIDATOR.validate_package(repo_root, package / "aw.yml")

    def test_rejects_grader_evaluator_symlink_escape(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            repo_root = root / "repo"
            evaluator = repo_root / ".github" / "graders" / "escape.sh"
            outside = root / "outside.sh"
            evaluator.parent.mkdir(parents=True)
            outside.write_text("#!/usr/bin/env bash\n", encoding="utf-8")
            try:
                evaluator.symlink_to(outside)
            except OSError as error:
                self.skipTest(f"symbolic links are unavailable: {error}")

            with self.assertRaisesRegex(RuntimeError, "must not be a symbolic link"):
                VALIDATOR.resolve_grader_evaluator(
                    repo_root,
                    repo_root / ".github" / "workflows" / "example.md",
                    Path(".github/workflows/example.md"),
                    ".github/graders/escape.sh",
                )

    def test_stages_workflow_local_grader_beside_installed_workflow(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            repo_root = Path(temp_dir)
            package = repo_root / "agentic-workflows" / "example"
            workflow = package / "workflows" / "example.md"
            evaluator = package / "workflows" / "graders" / "local.sh"
            evaluator.parent.mkdir(parents=True)
            (package / "aw.yml").write_text(
                """includes:
  - workflows/example.md
""",
                encoding="utf-8",
            )
            workflow.write_text(
                """---
on: workflow_dispatch
graders:
  operational-value:
    run: ./graders/local.sh
---
""",
                encoding="utf-8",
            )
            evaluator.write_text("#!/usr/bin/env bash\nprintf '[]\\n'\n", encoding="utf-8")

            def verify_staging(command: list[str], cwd: Path) -> None:
                if command[:3] != ["gh", "aw", "compile"]:
                    return
                self.assertEqual(
                    (
                        cwd
                        / ".github"
                        / "workflows"
                        / "graders"
                        / evaluator.name
                    ).read_bytes(),
                    evaluator.read_bytes(),
                )

            with mock.patch.object(VALIDATOR, "run", side_effect=verify_staging):
                VALIDATOR.validate_package(repo_root, package / "aw.yml")


class ActiveWorkflowTests(unittest.TestCase):
    def test_active_workflows_use_fixed_copilot_proxy(self) -> None:
        workflows = MODULE_PATH.parents[2] / ".github" / "workflows"
        locks = VALIDATOR.expected_active_locks(workflows)
        self.assertTrue(locks)
        for lock in sorted(locks):
            with self.subTest(workflow=lock.name):
                manifest = json.loads(
                    lock.read_text(encoding="utf-8")
                    .splitlines()[1]
                    .removeprefix("# gh-aw-manifest: ")
                )
                proxies = [
                    container["image"]
                    for container in manifest["containers"]
                    if container["image"].startswith(
                        "ghcr.io/github/gh-aw-firewall/api-proxy:"
                    )
                ]
                self.assertEqual(len(proxies), 1)
                version = tuple(map(int, proxies[0].rsplit(":", 1)[1].split(".")))
                self.assertGreaterEqual(
                    version,
                    (0, 28, 25),
                    "Older proxies corrupt custom-tool requests or replay IDs",
                )

    def test_packages_require_the_validated_compiler(self) -> None:
        packages = MODULE_PATH.parents[2] / "agentic-workflows"
        manifests = sorted(packages.rglob("aw.yml"))
        self.assertTrue(manifests)
        for manifest in manifests:
            with self.subTest(package=manifest.parent.name):
                data = VALIDATOR.yaml.safe_load(manifest.read_text(encoding="utf-8"))
                self.assertEqual(data["min-version"], VALIDATOR.GH_AW_VERSION)

    def test_detects_block_and_inline_triggers(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            workflows = root / ".github" / "workflows"
            workflows.mkdir(parents=True)
            sources = {
                "block.md": "---\non:\n  workflow_dispatch:\n---\n",
                "scalar.md": "---\non: workflow_dispatch\n---\n",
                "flow.md": "---\non: [push, pull_request]\n---\n",
                "fragment.md": "---\ndescription: shared fragment\n---\n",
            }
            for name, content in sources.items():
                (workflows / name).write_text(content, encoding="utf-8")

            self.assertEqual(
                {path.name for path in VALIDATOR.expected_active_locks(workflows)},
                {"block.lock.yml", "scalar.lock.yml", "flow.lock.yml"},
            )


class LocalPackageStagingTests(unittest.TestCase):
    def test_rejects_manifest_symlink(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            package = root / "agentic-workflows" / "example"
            consumer = root / "consumer"
            package.mkdir(parents=True)
            consumer.mkdir()
            target = package / "real-aw.yml"
            manifest = package / "aw.yml"
            target.write_text("includes: []\n", encoding="utf-8")
            try:
                manifest.symlink_to(target)
            except OSError as error:
                self.skipTest(f"symbolic links are unavailable: {error}")

            with self.assertRaisesRegex(
                RuntimeError, "Package manifest must not be a symbolic link"
            ):
                STAGER.stage_package(manifest, consumer)

    def test_rejects_include_symlink_without_partial_copy(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            package = root / "agentic-workflows" / "example"
            consumer = root / "consumer"
            workflow = package / "workflows" / "example.md"
            workflow_target = package / "workflows" / "real.md"
            agent = package / "agents" / "example.agent.md"
            workflow.parent.mkdir(parents=True)
            agent.parent.mkdir(parents=True)
            consumer.mkdir()
            manifest = package / "aw.yml"
            manifest.write_text(
                """includes:
  - agents/example.agent.md
  - workflows/example.md
""",
                encoding="utf-8",
            )
            workflow_target.write_text(
                "---\non: workflow_dispatch\n---\n", encoding="utf-8"
            )
            agent.write_text("---\nname: example\n---\n", encoding="utf-8")
            try:
                workflow.symlink_to(workflow_target)
            except OSError as error:
                self.skipTest(f"symbolic links are unavailable: {error}")

            with self.assertRaisesRegex(
                RuntimeError, "Package include component must not be a symbolic link"
            ):
                STAGER.stage_package(manifest, consumer)

            self.assertFalse(
                (consumer / ".github" / "agents" / "example.agent.md").exists()
            )
            self.assertFalse(
                (consumer / ".github" / "workflows" / "example.md").exists()
            )

    def test_rejects_symlinked_include_parent_without_partial_copy(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            package = root / "agentic-workflows" / "example"
            consumer = root / "consumer"
            outside = root / "outside"
            workflows = package / "workflows"
            workflow = outside / "example.md"
            workflows.mkdir(parents=True)
            consumer.mkdir()
            outside.mkdir()
            manifest = package / "aw.yml"
            manifest.write_text(
                "includes:\n  - workflows/link/example.md\n", encoding="utf-8"
            )
            workflow.write_text("---\non: workflow_dispatch\n---\n", encoding="utf-8")
            try:
                (workflows / "link").symlink_to(outside, target_is_directory=True)
            except OSError as error:
                self.skipTest(f"symbolic links are unavailable: {error}")

            with self.assertRaisesRegex(
                RuntimeError, "Package include component must not be a symbolic link"
            ):
                STAGER.stage_package(manifest, consumer)

            self.assertFalse(
                (consumer / ".github" / "workflows" / "link" / "example.md").exists()
            )

    def test_rejects_symlinked_grader_parent_without_partial_copy(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            repo_root = Path(temp_dir)
            package = repo_root / "agentic-workflows" / "example"
            consumer = repo_root / "consumer"
            outside = repo_root / "outside"
            workflow = package / "workflows" / "example.md"
            grader_root = repo_root / ".github" / "graders"
            workflow.parent.mkdir(parents=True)
            grader_root.parent.mkdir(parents=True)
            consumer.mkdir()
            outside.mkdir()
            manifest = package / "aw.yml"
            manifest.write_text("includes:\n  - workflows/example.md\n", encoding="utf-8")
            workflow.write_text(
                """---
on: workflow_dispatch
graders:
  operational-value:
    run: .github/graders/example.sh
---
""",
                encoding="utf-8",
            )
            (outside / "example.sh").write_text("#!/usr/bin/env bash\n", encoding="utf-8")
            try:
                grader_root.symlink_to(outside, target_is_directory=True)
            except OSError as error:
                self.skipTest(f"symbolic links are unavailable: {error}")

            with self.assertRaisesRegex(
                RuntimeError, "Package grader component must not be a symbolic link"
            ):
                STAGER.stage_package(manifest, consumer)

            self.assertFalse(
                (consumer / ".github" / "workflows" / "example.md").exists()
            )
            self.assertFalse(
                (consumer / ".github" / "graders" / "example.sh").exists()
            )

    def test_rejects_symlinked_destination_parent_without_partial_copy(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            package = root / "agentic-workflows" / "example"
            consumer = root / "consumer"
            outside = root / "outside"
            workflow = package / "workflows" / "example.md"
            agent = package / "agents" / "example.agent.md"
            evaluator = root / ".github" / "graders" / "example.sh"
            workflow.parent.mkdir(parents=True)
            agent.parent.mkdir(parents=True)
            evaluator.parent.mkdir(parents=True)
            (consumer / ".github").mkdir(parents=True)
            outside.mkdir()
            manifest = package / "aw.yml"
            manifest.write_text(
                """includes:
  - agents/example.agent.md
  - workflows/example.md
""",
                encoding="utf-8",
            )
            workflow.write_text(
                """---
on: workflow_dispatch
graders:
  operational-value:
    run: .github/graders/example.sh
---
""",
                encoding="utf-8",
            )
            agent.write_text("---\nname: example\n---\n", encoding="utf-8")
            evaluator.write_text("#!/usr/bin/env bash\n", encoding="utf-8")
            try:
                (consumer / ".github" / "graders").symlink_to(
                    outside, target_is_directory=True
                )
            except OSError as error:
                self.skipTest(f"symbolic links are unavailable: {error}")

            with self.assertRaisesRegex(
                RuntimeError,
                "Consumer destination component must not be a symbolic link",
            ):
                STAGER.stage_package(manifest, consumer)

            self.assertFalse((outside / "example.sh").exists())
            self.assertFalse(
                (consumer / ".github" / "agents" / "example.agent.md").exists()
            )
            self.assertFalse(
                (consumer / ".github" / "workflows" / "example.md").exists()
            )

    def test_rejects_destination_parent_symlink_inside_consumer(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            package = root / "agentic-workflows" / "example"
            consumer = root / "consumer"
            redirected = consumer / "redirected"
            workflow = package / "workflows" / "example.md"
            workflow.parent.mkdir(parents=True)
            (consumer / ".github").mkdir(parents=True)
            redirected.mkdir()
            manifest = package / "aw.yml"
            manifest.write_text("includes:\n  - workflows/example.md\n", encoding="utf-8")
            workflow.write_text("---\non: workflow_dispatch\n---\n", encoding="utf-8")
            try:
                (consumer / ".github" / "workflows").symlink_to(
                    redirected, target_is_directory=True
                )
            except OSError as error:
                self.skipTest(f"symbolic links are unavailable: {error}")

            with self.assertRaisesRegex(
                RuntimeError,
                "Consumer destination component must not be a symbolic link",
            ):
                STAGER.stage_package(manifest, consumer)

            self.assertFalse((redirected / "example.md").exists())

    def test_rejects_symlinked_consumer_root_without_outside_copy(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            package = root / "agentic-workflows" / "example"
            outside = root / "outside"
            consumer_link = root / "consumer-link"
            workflow = package / "workflows" / "example.md"
            workflow.parent.mkdir(parents=True)
            outside.mkdir()
            manifest = package / "aw.yml"
            manifest.write_text("includes:\n  - workflows/example.md\n", encoding="utf-8")
            workflow.write_text("---\non: workflow_dispatch\n---\n", encoding="utf-8")
            try:
                consumer_link.symlink_to(outside, target_is_directory=True)
            except OSError as error:
                self.skipTest(f"symbolic links are unavailable: {error}")

            with self.assertRaisesRegex(
                RuntimeError,
                "Consumer directory must not be a symbolic link",
            ):
                STAGER.stage_package(manifest, consumer_link)

            self.assertFalse(
                (outside / ".github" / "workflows" / "example.md").exists()
            )

    def test_stages_package_using_installed_layout(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            package = root / "agentic-workflows" / "example"
            consumer = root / "consumer"
            workflow = package / "workflows" / "example.md"
            agent = package / "agents" / "example.agent.md"
            workflow.parent.mkdir(parents=True)
            agent.parent.mkdir(parents=True)
            consumer.mkdir()
            manifest = package / "aw.yml"
            manifest.write_text(
                """includes:
  - workflows/example.md
  - agents/example.agent.md
""",
                encoding="utf-8",
            )
            workflow.write_text("---\non: workflow_dispatch\n---\n", encoding="utf-8")
            agent.write_text("---\nname: example\n---\n", encoding="utf-8")

            staged = STAGER.stage_package(manifest, consumer)

            self.assertEqual(
                staged,
                [
                    Path(".github/agents/example.agent.md"),
                    Path(".github/workflows/example.md"),
                ],
            )
            self.assertEqual(
                (consumer / ".github" / "workflows" / "example.md").read_bytes(),
                workflow.read_bytes(),
            )
            self.assertEqual(
                (consumer / ".github" / "agents" / "example.agent.md").read_bytes(),
                agent.read_bytes(),
            )

    def test_refuses_to_overwrite_conflicting_consumer_file(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            package = root / "agentic-workflows" / "example"
            consumer = root / "consumer"
            workflow = package / "workflows" / "example.md"
            destination = consumer / ".github" / "workflows" / "example.md"
            workflow.parent.mkdir(parents=True)
            destination.parent.mkdir(parents=True)
            manifest = package / "aw.yml"
            manifest.write_text("includes:\n  - workflows/example.md\n", encoding="utf-8")
            workflow.write_text("---\non: workflow_dispatch\n---\n", encoding="utf-8")
            destination.write_text("consumer\n", encoding="utf-8")

            with self.assertRaisesRegex(RuntimeError, "Refusing to overwrite"):
                STAGER.stage_package(manifest, consumer)

            self.assertEqual(destination.read_text(encoding="utf-8"), "consumer\n")


if __name__ == "__main__":
    unittest.main()
