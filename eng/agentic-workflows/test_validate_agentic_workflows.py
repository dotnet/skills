#!/usr/bin/env python3
"""Tests for the agentic workflow validation helpers."""

from __future__ import annotations

import importlib.util
import tempfile
import unittest
from pathlib import Path


MODULE_PATH = Path(__file__).with_name("validate_agentic_workflows.py")
SPEC = importlib.util.spec_from_file_location("validate_agentic_workflows", MODULE_PATH)
assert SPEC and SPEC.loader
VALIDATOR = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(VALIDATOR)


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


class ActiveWorkflowTests(unittest.TestCase):
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


if __name__ == "__main__":
    unittest.main()
