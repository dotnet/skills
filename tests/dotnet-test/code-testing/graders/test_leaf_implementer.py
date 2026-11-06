"""Deterministic orchestration contracts; no model or toolchain calls."""

import ast
from pathlib import Path
import re
import unittest


ROOT = Path(__file__).resolve().parents[4]
PLUGIN = ROOT / "plugins" / "dotnet-test"
PHASE_WORKERS = ("researcher", "planner", "implementer")
VALIDATION_WORKERS = ("builder", "tester", "fixer", "linter")


def agent(name):
    text = (PLUGIN / "agents" / f"{name}.agent.md").read_text(encoding="utf-8")
    frontmatter, body = text.removeprefix("---\n").split("\n---\n", 1)
    return frontmatter, body


class LeafImplementerTests(unittest.TestCase):
    def test_implementer_keeps_operational_tools_but_cannot_launch_children(self):
        frontmatter, _ = agent("code-testing-implementer")
        tools = ast.literal_eval(re.search(r"^tools: (.+)$", frontmatter, re.MULTILINE)[1])
        self.assertEqual(set(tools), {
            "skill", "read", "search", "edit", "execute",
            "Skill", "Read", "Glob", "Grep", "Edit", "Write", "Bash",
            "read_file", "replace", "write_file", "glob", "grep_search",
            "run_shell_command",
        })
        self.assertNotRegex(frontmatter, r"(?m)^agents:")
        self.assertIn("user-invocable: false", frontmatter)

    def test_implementer_owns_every_validation_step_inline(self):
        _, body = agent("code-testing-implementer")
        self.assertIn("do not launch or delegate to subagents", body)
        for command in (
            "Run the supplied scoped build command directly.",
            "diagnose and fix the changed tests yourself.",
            "Run the supplied scoped test command directly.",
            "run the command it specifies *from the repo root*",
            "run the repository's existing command\ndirectly for the changed tests",
            "apply the contract yourself rather than relying on console-green.",
        ):
            with self.subTest(command=command):
                self.assertIn(command, body)
        for worker in VALIDATION_WORKERS:
            with self.subTest(worker=worker):
                self.assertNotIn(f"code-testing-{worker}", body)
        self.assertNotIn("delegated tester", body)

    def test_leaf_preserves_quality_boundaries_discovery_and_stop_conditions(self):
        _, body = agent("code-testing-implementer")
        self.assertEqual(re.findall(r"^### (\d+)\.", body, re.MULTILINE),
                         [str(number) for number in range(1, 10)])
        for requirement in (
            "work with any programming language",
            "never invoke the public `code-testing` skill",
            "or delegate back to `test-engineer`",
            "return `PARTIAL` with the exact blocker",
            "**Validate project references**",
            '**Validate project-system registration**',
            '<Compile Include="...">',
            "**Capture the baseline test count**",
            "Register every new project **and every new file",
            "**Existing test files are append-only.**",
            "**Do not modify non-test source files.**",
            "**Keep intermediate state files non-stageable.**",
            "<TESTAGENT_DIR>",
            "**Never revert or clean the working tree.**",
            "**One exception**: build-system manifests",
            "Never convert `packages.config` to `PackageReference`",
            "one mutation-relevant case for every distinct observable",
            "Mock all external dependencies",
            "Report-safe test names and result validation",
            "TypeScript transpilation alone does not satisfy the build gate",
            "at most three times",
            "repeats without progress",
            "Permission/toolchain blockers are not assertion",
            "Never mark a test `[Ignore]`, `[Skip]`, or `[Inconclusive]`",
            "at most five attempts",
            "Do not proceed to Step 8 until the harness-equivalent command sees your new tests.",
            "STATUS: SUCCESS | PARTIAL | FAILED",
            "HARNESS_DISCOVERY: [count delta from Step 7]",
            "shared report-safe naming and result-validation contract",
        ):
            with self.subTest(requirement=requirement):
                self.assertIn(" ".join(requirement.split()), " ".join(body.split()))

    def test_pipeline_owner_keeps_optional_phase_and_validation_workers(self):
        frontmatter, body = agent("test-engineer")
        self.assertIn("user-invocable: true", frontmatter)
        for worker in (*PHASE_WORKERS, *VALIDATION_WORKERS):
            name = f"code-testing-{worker}"
            with self.subTest(worker=name):
                self.assertIn(f"  - {name}\n", frontmatter)
                worker_frontmatter, _ = agent(name)
                self.assertIn("user-invocable: false", worker_frontmatter)
        self.assertIn("Phases are not agent calls.", body)
        self.assertIn("keeping the phases inline unless substantial separate work warrants delegation", body)

    def test_docs_show_relative_leaf_boundary_and_unchanged_entry_routing(self):
        readme = (PLUGIN / "README.md").read_text(encoding="utf-8")
        for worker in (*PHASE_WORKERS, *VALIDATION_WORKERS):
            with self.subTest(worker=worker):
                self.assertIn(f"| **code-testing-{worker}** | test-engineer |", readme)
        self.assertIn("not a global session-depth limit", readme)
        skill = (PLUGIN / "skills" / "code-testing" / "SKILL.md").read_text(encoding="utf-8")
        diagram = skill.split("### Pipeline Overview\n", 1)[1].split("```text\n", 1)[1].split("```", 1)[0]
        self.assertIn("IMPLEMENTER (leaf)", diagram)
        self.assertIn("Checks discovery", diagram)
        self.assertNotRegex(diagram, r"BUILDER|TESTER|FIXER|LINTER")
        self.assertIn("focused work directly and invokes the public `test-engineer`", skill)
        self.assertIn("When `test-engineer` is\n  available, invoke that named custom agent", skill)
        self.assertIn("do not create intermediate state files or fan out to multiple\n  agents", skill)
        self.assertIn("If `test-engineer` is unavailable, do not skip the workflow.", skill)
        self.assertNotIn("maxDepth", skill + readme + agent("code-testing-implementer")[1])


if __name__ == "__main__":
    unittest.main()
