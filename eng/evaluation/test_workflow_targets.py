#!/usr/bin/env python3
"""Exercise workflow discovery and dispatch through the real PowerShell scripts."""

import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import unittest

import yaml
from test_token_failover import BASH

REPO_ROOT = Path(__file__).resolve().parents[2]


def normalized_output(result):
    output = re.sub(r"\x1b\[[0-?]*[ -/]*[@-~]", "", result.stdout + result.stderr)
    output = re.sub(r"(?m)^\s*\|\s?", "", output)
    return " ".join(output.split())


class WorkflowTargetsTests(unittest.TestCase):
    def setUp(self):
        self.scratch = tempfile.TemporaryDirectory(prefix="workflow-targets-")
        self.addCleanup(self.scratch.cleanup)
        self.root = Path(self.scratch.name)
        scripts = self.root / "eng" / "evaluation"
        scripts.mkdir(parents=True)
        for name in (
            "change-classification.ps1",
            "path-safety.ps1",
            "workflow-targets.ps1",
            "find-targets.ps1",
        ):
            shutil.copy2(REPO_ROOT / "eng" / "evaluation" / name, scripts / name)
        (self.root / "plugins").mkdir()
        for package in ("alpha", "beta"):
            source = self.root / "agentic-workflows" / package
            source.mkdir(parents=True)
            (source / "aw.yml").write_text("includes: [workflows/main.md]\n", encoding="utf-8")
            tests = self.root / "tests" / "agentic-workflows" / package
            tests.mkdir(parents=True)
            (tests / "eval.yaml").write_text("name: fixture\nstimuli: []\n", encoding="utf-8")
        workflow = yaml.safe_load(
            (REPO_ROOT / ".github" / "workflows" / "evaluation-run.yml").read_text(encoding="utf-8"))
        self.dispatch = next(step["run"] for step in workflow["jobs"]["prepare"]["steps"]
                             if step.get("id") == "build")
        self.matrix_validation = next(step["run"] for step in workflow["jobs"]["vally-evaluate"]["steps"]
                                      if step.get("name") == "Validate matrix entry")
        self.output = self.root / "output.txt"

    def run_script(self, script, **environment):
        self.output.unlink(missing_ok=True)
        env = dict(os.environ, GITHUB_OUTPUT=str(self.output), **environment)
        return subprocess.run(
            ["pwsh", "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", script],
            cwd=self.root, env=env, text=True, capture_output=True, timeout=30)

    def entries(self):
        lines = self.output.read_text(encoding="utf-8").splitlines()
        return json.loads(next(line.removeprefix("entries=") for line in lines if line.startswith("entries=")))

    def git(self, *args):
        result = subprocess.run(["git", *args], cwd=self.root, text=True, capture_output=True, timeout=30)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        return result.stdout.strip()

    def commit_fixture(self, message):
        self.git("add", ".")
        self.git("-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid",
                 "commit", "--quiet", "-m", message)
        return self.git("rev-parse", "HEAD")

    def create_plugin_fixture(self):
        manifest = self.root / "plugins" / "demo" / "plugin.json"
        skill = self.root / "plugins" / "demo" / "skills" / "sample" / "SKILL.md"
        evaluation = self.root / "tests" / "demo" / "sample" / "eval.yaml"
        manifest.parent.mkdir(parents=True)
        skill.parent.mkdir(parents=True)
        evaluation.parent.mkdir(parents=True)
        manifest.write_text(
            '{"name":"demo","version":"1.0.0","skills":["./skills/"]}\n',
            encoding="utf-8",
        )
        skill.write_text(
            "---\nname: sample\ndescription: Sample.\n---\nSample.\n",
            encoding="utf-8",
        )
        evaluation.write_text(
            "name: sample\nstimuli: []\n",
            encoding="utf-8",
        )
        return manifest

    def create_agent_fixture(self, manifest_content=None):
        agent = self.root / "plugins" / "demo" / "agents" / "router.agent.md"
        evaluation = self.root / "tests" / "demo" / "agent.router" / "eval.yaml"
        agent.parent.mkdir(parents=True)
        evaluation.parent.mkdir(parents=True)
        agent.write_text(
            "---\nname: router\ndescription: Route work.\n---\nRoute work.\n",
            encoding="utf-8",
        )
        evaluation.write_text(
            "name: router\nstimuli: []\n",
            encoding="utf-8",
        )
        if manifest_content is not None:
            manifest = self.root / "plugins" / "demo" / "plugin.json"
            manifest.write_text(manifest_content, encoding="utf-8")
        return agent

    def test_manual_collection_and_single_package_dispatch(self):
        for selected, count in (("", 2), ("alpha", 1)):
            with self.subTest(selected=selected):
                result = self.run_script(self.dispatch, PLUGIN="agentic-workflows", SKILL=selected)
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                entries = self.entries()
                self.assertEqual(len(entries), count)
                for entry in entries:
                    self.assertEqual(entry["target_kind"], "workflow")
                    self.assertEqual(entry["plugin"], "agentic-workflows")
                    package = entry["package_path"].split("/")[1]
                    self.assertEqual(entry["eval_path"], f"tests/agentic-workflows/{package}/eval.yaml")

    def test_scheduled_discovery_includes_workflows_in_model_matrix(self):
        result = self.run_script(
            "& .\\eng\\evaluation\\find-targets.ps1",
            EVAL_EVENT_NAME="schedule", GATE_PR_NUMBER="", INPUT_PLUGIN="")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        entries = self.entries()
        self.assertEqual(len(entries), 4)
        self.assertEqual({entry["target_kind"] for entry in entries}, {"workflow"})
        self.assertTrue(all(entry.get("model") and entry.get("judge") for entry in entries))

    def test_manual_top_level_discovery_accepts_workflow_collection(self):
        result = self.run_script(
            "& .\\eng\\evaluation\\find-targets.ps1",
            EVAL_EVENT_NAME="workflow_dispatch", GATE_PR_NUMBER="", INPUT_PLUGIN="agentic-workflows")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(len(self.entries()), 4)

    def test_pr_package_resource_change_selects_only_its_package_and_cleans_worktree(self):
        def git(*args):
            result = subprocess.run(["git", *args], cwd=self.root, text=True, capture_output=True, timeout=30)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            return result.stdout.strip()

        git("init", "--quiet")
        git("add", ".")
        git("-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "commit", "--quiet", "-m", "Fixture base")
        base = git("rev-parse", "HEAD")
        resource = self.root / "agentic-workflows" / "alpha" / "workflows" / "helper.json"
        resource.parent.mkdir()
        resource.write_text("{}\n", encoding="utf-8")
        git("add", ".")
        git("-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "commit", "--quiet", "-m", "Change package resource")
        head = git("rev-parse", "HEAD")
        result = self.run_script(
            "& .\\eng\\evaluation\\find-targets.ps1", GATE_PR_NUMBER="1",
            GATE_BASE_SHA=base, GATE_HEAD_SHA=head, EVAL_EVENT_NAME="workflow_dispatch", INPUT_PLUGIN="")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual({entry["package_path"] for entry in self.entries()}, {"agentic-workflows/alpha/aw.yml"})
        self.assertEqual(git("worktree", "list", "--porcelain").count("worktree "), 1)

    def test_pr_shared_workflow_inputs_select_all_packages(self):
        self.git("init", "--quiet")
        base = self.commit_fixture("Fixture base")
        for relative in (
            "tests/agentic-workflows/graders/check_result.py",
            "tests/agentic-workflows/RESULT_SCHEMA.md",
            "tests/agentic-workflows/test_graders.py",
            "tests/agentic-workflows/make_workflow_contexts.py",
        ):
            with self.subTest(relative=relative):
                shared = self.root / relative
                shared.parent.mkdir(parents=True, exist_ok=True)
                shared.write_text("shared input\n", encoding="utf-8")
                head = self.commit_fixture("Change shared workflow input")
                result = self.run_script(
                    "& .\\eng\\evaluation\\find-targets.ps1", GATE_PR_NUMBER="1",
                    GATE_BASE_SHA=base, GATE_HEAD_SHA=head, EVAL_EVENT_NAME="workflow_dispatch", INPUT_PLUGIN="")
                base = head
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                entries = self.entries()
                self.assertEqual({entry["package_path"] for entry in entries},
                                 {"agentic-workflows/alpha/aw.yml", "agentic-workflows/beta/aw.yml"})
                self.assertEqual(len(entries), 4)
                self.assertEqual(self.git("worktree", "list", "--porcelain").count("worktree "), 1)

    def test_pr_malformed_changed_manifest_fails_target_discovery(self):
        manifest = self.create_plugin_fixture()
        self.git("init", "--quiet")
        base = self.commit_fixture("Fixture base")
        manifest.write_text("{ malformed", encoding="utf-8")
        head = self.commit_fixture("Break manifest")

        result = self.run_script(
            "& .\\eng\\evaluation\\find-targets.ps1",
            GATE_PR_NUMBER="1",
            GATE_BASE_SHA=base,
            GATE_HEAD_SHA=head,
            EVAL_EVENT_NAME="workflow_dispatch",
            INPUT_PLUGIN="",
        )

        self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
        output = normalized_output(result)
        self.assertIn("target discovery cannot enumerate", output)
        self.assertIn("production surface", output)

    def test_pr_valid_manifest_without_agents_keeps_empty_agent_result(self):
        manifest = self.create_plugin_fixture()
        self.git("init", "--quiet")
        base = self.commit_fixture("Fixture base")
        manifest.write_text(
            '{"name":"demo","version":"1.0.1",'
            '"description":"Changed","skills":["./skills/"]}\n',
            encoding="utf-8",
        )
        head = self.commit_fixture("Change manifest")

        result = self.run_script(
            "& .\\eng\\evaluation\\find-targets.ps1",
            GATE_PR_NUMBER="1",
            GATE_BASE_SHA=base,
            GATE_HEAD_SHA=head,
            EVAL_EVENT_NAME="workflow_dispatch",
            INPUT_PLUGIN="",
        )

        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        entries = self.entries()
        self.assertTrue(entries)
        self.assertEqual(
            {entry["target_kind"] for entry in entries},
            {"skill"},
        )

    def test_pr_fully_deleted_plugin_has_no_surviving_target(self):
        manifest = self.create_plugin_fixture()
        self.git("init", "--quiet")
        base = self.commit_fixture("Fixture base")
        shutil.rmtree(manifest.parent)
        shutil.rmtree(self.root / "tests" / "demo")
        head = self.commit_fixture("Remove manifest")

        result = self.run_script(
            "& .\\eng\\evaluation\\find-targets.ps1",
            GATE_PR_NUMBER="1",
            GATE_BASE_SHA=base,
            GATE_HEAD_SHA=head,
            EVAL_EVENT_NAME="workflow_dispatch",
            INPUT_PLUGIN="",
        )

        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(self.entries(), [])

    def test_pr_removed_manifest_from_surviving_plugin_fails_closed(self):
        manifest = self.create_plugin_fixture()
        self.git("init", "--quiet")
        base = self.commit_fixture("Fixture base")
        manifest.unlink()
        head = self.commit_fixture("Remove manifest")

        result = self.run_script(
            "& .\\eng\\evaluation\\find-targets.ps1",
            GATE_PR_NUMBER="1",
            GATE_BASE_SHA=base,
            GATE_HEAD_SHA=head,
            EVAL_EVENT_NAME="workflow_dispatch",
            INPUT_PLUGIN="",
        )

        self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
        output = normalized_output(result)
        self.assertIn("target discovery cannot enumerate", output)
        self.assertIn("production surface", output)

    def test_pr_merge_base_failure_fails_closed(self):
        self.git("init", "--quiet")
        head = self.commit_fixture("Fixture base")

        result = self.run_script(
            "& .\\eng\\evaluation\\find-targets.ps1",
            GATE_PR_NUMBER="1",
            GATE_BASE_SHA="not-a-commit",
            GATE_HEAD_SHA=head,
            EVAL_EVENT_NAME="workflow_dispatch",
            INPUT_PLUGIN="",
        )

        self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn(
            "Could not determine a merge base",
            normalized_output(result),
        )

    def test_pr_diff_failure_fails_closed(self):
        self.git("init", "--quiet")
        base = self.commit_fixture("Fixture base")
        changed = self.root / "changed.txt"
        changed.write_text("changed\n", encoding="utf-8")
        head = self.commit_fixture("Change file")

        real_git = shutil.which("git")
        self.assertIsNotNone(real_git)
        wrapper_dir = self.root / "fake-bin"
        wrapper_dir.mkdir()
        if os.name == "nt":
            wrapper = wrapper_dir / "git.cmd"
            wrapper.write_text(
                '@echo off\r\n'
                'if "%1"=="diff" exit /b 42\r\n'
                f'"{real_git}" %*\r\n',
                encoding="utf-8",
            )
        else:
            wrapper = wrapper_dir / "git"
            wrapper.write_text(
                "#!/bin/sh\n"
                'if [ "$1" = "diff" ]; then exit 42; fi\n'
                f'exec "{real_git}" "$@"\n',
                encoding="utf-8",
            )
            wrapper.chmod(0o755)

        result = self.run_script(
            "& .\\eng\\evaluation\\find-targets.ps1",
            GATE_PR_NUMBER="1",
            GATE_BASE_SHA=base,
            GATE_HEAD_SHA=head,
            EVAL_EVENT_NAME="workflow_dispatch",
            INPUT_PLUGIN="",
            PATH=str(wrapper_dir) + os.pathsep + os.environ["PATH"],
        )

        self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn(
            "Could not enumerate changed files",
            normalized_output(result),
        )

    def test_agent_source_change_with_missing_manifest_fails_closed(self):
        agent = self.create_agent_fixture()
        self.git("init", "--quiet")
        base = self.commit_fixture("Fixture base")
        agent.write_text(agent.read_text(encoding="utf-8") + "\nChanged.\n",
                         encoding="utf-8")
        head = self.commit_fixture("Change agent")

        result = self.run_script(
            "& .\\eng\\evaluation\\find-targets.ps1",
            GATE_PR_NUMBER="1",
            GATE_BASE_SHA=base,
            GATE_HEAD_SHA=head,
            EVAL_EVENT_NAME="workflow_dispatch",
            INPUT_PLUGIN="",
        )

        self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("plugin.json is missing", result.stdout + result.stderr)

    def test_agent_source_change_with_invalid_manifest_fails_closed(self):
        agent = self.create_agent_fixture("{ malformed")
        self.git("init", "--quiet")
        base = self.commit_fixture("Fixture base")
        agent.write_text(agent.read_text(encoding="utf-8") + "\nChanged.\n",
                         encoding="utf-8")
        head = self.commit_fixture("Change agent")

        result = self.run_script(
            "& .\\eng\\evaluation\\find-targets.ps1",
            GATE_PR_NUMBER="1",
            GATE_BASE_SHA=base,
            GATE_HEAD_SHA=head,
            EVAL_EVENT_NAME="workflow_dispatch",
            INPUT_PLUGIN="",
        )

        self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("plugin.json is invalid", result.stdout + result.stderr)

    def test_missing_spec_unknown_package_and_traversal_fail_closed(self):
        for selected in ("missing", "../alpha", "alpha/.."):
            with self.subTest(selected=selected):
                result = self.run_script(self.dispatch, PLUGIN="agentic-workflows", SKILL=selected)
                self.assertNotEqual(result.returncode, 0, result.stdout)
        (self.root / "tests" / "agentic-workflows" / "alpha" / "eval.yaml").unlink()
        result = self.run_script(self.dispatch, PLUGIN="agentic-workflows", SKILL="alpha")
        self.assertNotEqual(result.returncode, 0, result.stdout)
        self.assertIn("has no tests/agentic-workflows/alpha/eval.yaml", result.stderr)

    def test_runner_matrix_validation_binds_workflow_identity(self):
        if not shutil.which(BASH):
            self.skipTest("Bash is required for runner matrix validation")
        script = self.root / "matrix-validation.sh"
        script.write_text(self.matrix_validation, encoding="utf-8")
        environment = dict(
            os.environ, ENTRY_PLUGIN="agentic-workflows", ENTRY_NAME="agentic-workflows--alpha--executor",
            ENTRY_TARGET_KIND="workflow", ENTRY_PACKAGE_PATH="agentic-workflows/alpha/aw.yml",
            ENTRY_SKILLS_PATH="", ENTRY_AGENTS_PATH="", ENTRY_EVAL_PATH="tests/agentic-workflows/alpha/eval.yaml",
            ENTRY_MODEL="executor", ENTRY_JUDGE="judge")
        for changes, success in (
            ({}, True),
            ({"ENTRY_PACKAGE_PATH": "agentic-workflows/../aw.yml"}, False),
            ({"ENTRY_EVAL_PATH": "tests/agentic-workflows/beta/eval.yaml"}, False),
            ({"ENTRY_NAME": "agentic-workflows--beta--executor"}, False),
            ({"ENTRY_AGENTS_PATH": "plugins/agentic-workflows/agents/other.agent.md"}, False),
        ):
            with self.subTest(changes=changes):
                result = subprocess.run(
                    [BASH, "matrix-validation.sh"],
                    cwd=self.root, env=dict(environment, **changes), text=True, capture_output=True, timeout=30)
                self.assertEqual(result.returncode == 0, success, result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()
