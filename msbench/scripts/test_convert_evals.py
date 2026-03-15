#!/usr/bin/env python3
"""
test_convert_evals.py — Unit tests for the eval.yaml → Harbor task converter.
Run with: python -m pytest msbench/scripts/test_convert_evals.py -v
"""

import os
import shutil
import sys
import tempfile
from pathlib import Path

import pytest
import yaml

# Add scripts directory to path
sys.path.insert(0, str(Path(__file__).parent))
from convert_evals import (
    EXCLUDED_SKILLS,
    slugify,
    determine_difficulty,
    generate_task_toml,
    generate_instruction_md,
    generate_dockerfile,
    generate_test_sh,
    generate_solve_sh,
    resolve_fixture_path,
    process_scenario,
    discover_skills,
    convert_all,
)


# ============================================================================
# slugify tests
# ============================================================================


class TestSlugify:
    def test_basic_slugify(self):
        assert slugify("Hello World") == "hello-world"

    def test_strips_detect_prefix(self):
        result = slugify("Detects compiled regex startup budget")
        assert "detect" not in result
        assert "compiled" in result

    def test_strips_find_prefix(self):
        result = slugify("Finds per-call Dictionary allocation")
        assert "find" not in result
        assert "per-call" in result

    def test_strips_catch_prefix(self):
        result = slugify("Catches compound allocations in recursive converter")
        assert "catch" not in result

    def test_strips_filler_words(self):
        result = slugify("Detects compiled regex startup budget and regex chain allocations")
        assert result == "compiled-regex-startup-budget-regex-chain"

    def test_special_characters(self):
        result = slugify("Generate LibraryImport declaration from C header (.NET 8+)")
        assert "net-8" in result
        assert "(" not in result
        assert ")" not in result
        assert "+" not in result

    def test_dotnet_framework_suffix(self):
        result = slugify("Generate LibraryImport declaration from C header (.NET Framework)")
        assert "net-framework" in result

    def test_truncation_long_name(self):
        result = slugify("A" * 200)
        assert len(result) <= 50

    def test_no_leading_trailing_hyphens(self):
        result = slugify("---hello world---")
        assert not result.startswith("-")
        assert not result.endswith("-")

    def test_collapse_multiple_hyphens(self):
        result = slugify("hello    world")
        assert "--" not in result

    def test_modernize_prefix(self):
        result = slugify("Modernize legacy project to SDK-style")
        assert "modernize" not in result

    def test_analyze_prefix(self):
        result = slugify("Analyze incremental build issues")
        assert "analyze" not in result.lower()

    def test_real_scenario_names(self):
        """Test with actual scenario names from the repo."""
        test_cases = {
            "Detects compiled regex startup budget and regex chain allocations":
                "compiled-regex-startup-budget-regex-chain",
            "Worker thread with abort-based cancellation":
                "worker-thread-abort-based-cancellation",
            "Modernize legacy project to SDK-style":
                "legacy-project-sdk-style",
            "Diagnose bin/obj output path clashes":
                "bin-obj-output-path-clashes",
        }
        for name, expected in test_cases.items():
            result = slugify(name)
            assert result == expected, f"slugify({name!r}) = {result!r}, expected {expected!r}"


# ============================================================================
# determine_difficulty tests
# ============================================================================


class TestDetermineDifficulty:
    def test_easy_single_assertion_no_rubric(self):
        scenario = {"assertions": [{"type": "output_contains", "value": "foo"}]}
        assert determine_difficulty(scenario) == "easy"

    def test_easy_two_assertions_no_rubric(self):
        scenario = {
            "assertions": [
                {"type": "output_contains", "value": "foo"},
                {"type": "file_exists", "path": "*.txt"},
            ]
        }
        assert determine_difficulty(scenario) == "easy"

    def test_easy_exit_success_only(self):
        scenario = {"assertions": [{"type": "exit_success"}]}
        assert determine_difficulty(scenario) == "easy"

    def test_hard_with_rubric(self):
        scenario = {
            "assertions": [{"type": "output_contains", "value": "foo"}],
            "rubric": ["criterion 1"],
        }
        assert determine_difficulty(scenario) == "hard"

    def test_hard_many_assertions(self):
        scenario = {
            "assertions": [
                {"type": "output_contains", "value": "a"},
                {"type": "output_contains", "value": "b"},
                {"type": "output_contains", "value": "c"},
            ]
        }
        assert determine_difficulty(scenario) == "hard"

    def test_hard_exit_success_not_counted(self):
        """exit_success doesn't count toward the assertion threshold."""
        scenario = {
            "assertions": [
                {"type": "output_contains", "value": "a"},
                {"type": "exit_success"},
            ]
        }
        # 1 real assertion + exit_success = 1 real, no rubric → easy
        assert determine_difficulty(scenario) == "easy"

    def test_empty_scenario(self):
        assert determine_difficulty({}) == "easy"


# ============================================================================
# generate_task_toml tests
# ============================================================================


class TestGenerateTaskToml:
    def test_basic_toml(self):
        scenario = {"timeout": 120}
        result = generate_task_toml("dotnet", "analyzing-dotnet-performance", scenario, "hard")
        assert 'version = "1.0"' in result
        assert 'author_name = "dotnet/skills team"' in result
        assert 'difficulty = "hard"' in result
        assert "agent_timeout_sec = 120" in result
        assert '"skill:analyzing-dotnet-performance"' in result
        assert '"plugin:dotnet"' in result

    def test_default_timeout(self):
        scenario = {}
        result = generate_task_toml("dotnet", "some-skill", scenario, "easy")
        assert "agent_timeout_sec = 600" in result

    def test_msbuild_plugin_tag(self):
        scenario = {"timeout": 160}
        result = generate_task_toml("dotnet-msbuild", "check-bin-obj-clash", scenario, "hard")
        assert '"msbuild"' in result
        assert '"plugin:dotnet-msbuild"' in result


# ============================================================================
# generate_instruction_md tests
# ============================================================================


class TestGenerateInstructionMd:
    def test_verbatim_prompt(self):
        scenario = {"prompt": "Analyze this code for performance issues."}
        result = generate_instruction_md(scenario)
        assert result.strip() == "Analyze this code for performance issues."

    def test_multiline_prompt(self):
        scenario = {"prompt": "Line 1\nLine 2\nLine 3"}
        result = generate_instruction_md(scenario)
        assert "Line 1" in result
        assert "Line 3" in result

    def test_empty_prompt(self):
        result = generate_instruction_md({})
        assert result.strip() == ""


# ============================================================================
# generate_dockerfile tests
# ============================================================================


class TestGenerateDockerfile:
    def test_basic_dockerfile(self):
        result = generate_dockerfile({}, "test-task", False, False, [])
        assert "FROM mcr.microsoft.com/dotnet/sdk:9.0" in result
        assert "python3" in result
        assert "COPY eval_helpers/ /app/" in result

    def test_with_fixtures(self):
        result = generate_dockerfile({}, "test-task", True, False, [])
        assert "COPY fixtures/ /testbed/" in result

    def test_with_copy_test_files(self):
        result = generate_dockerfile({}, "test-task", False, True, [])
        assert "COPY test_files/ /testbed/" in result

    def test_with_setup_commands(self):
        cmds = ["dotnet restore", "dotnet build"]
        result = generate_dockerfile({}, "test-task", False, False, cmds)
        assert "RUN dotnet restore" in result
        assert "RUN dotnet build" in result


# ============================================================================
# generate_test_sh tests
# ============================================================================


class TestGenerateTestSh:
    def test_output_contains_assertion(self):
        assertions = [{"type": "output_contains", "value": "Compiled"}]
        result = generate_test_sh("test", "dotnet", "perf", assertions, [], "easy")
        assert "assert_output_contains 'Compiled'" in result

    def test_output_not_contains_assertion(self):
        assertions = [{"type": "output_not_contains", "value": "Error"}]
        result = generate_test_sh("test", "dotnet", "perf", assertions, [], "easy")
        assert "assert_output_not_contains 'Error'" in result

    def test_output_matches_assertion(self):
        assertions = [{"type": "output_matches", "pattern": "(foo|bar)"}]
        result = generate_test_sh("test", "dotnet", "perf", assertions, [], "easy")
        assert "assert_output_matches '(foo|bar)'" in result

    def test_output_not_matches_assertion(self):
        assertions = [{"type": "output_not_matches", "pattern": "bad.*pattern"}]
        result = generate_test_sh("test", "dotnet", "perf", assertions, [], "easy")
        assert "assert_output_not_matches 'bad.*pattern'" in result

    def test_file_exists_assertion(self):
        assertions = [{"type": "file_exists", "path": "*.csv"}]
        result = generate_test_sh("test", "dotnet", "perf", assertions, [], "easy")
        assert "assert_file_exists '*.csv'" in result

    def test_file_not_exists_assertion(self):
        assertions = [{"type": "file_not_exists", "path": "debug.log"}]
        result = generate_test_sh("test", "dotnet", "perf", assertions, [], "easy")
        assert "assert_file_not_exists 'debug.log'" in result

    def test_file_contains_assertion(self):
        assertions = [{"type": "file_contains", "path": "**/*.csproj", "value": "BenchmarkDotNet"}]
        result = generate_test_sh("test", "dotnet", "microbenchmarking", assertions, [], "hard")
        assert "assert_file_contains '**/*.csproj' 'BenchmarkDotNet'" in result

    def test_exit_success_assertion(self):
        assertions = [{"type": "exit_success"}]
        result = generate_test_sh("test", "dotnet", "perf", assertions, [], "easy")
        assert "assert_exit_success" in result

    def test_sources_assertion_runner(self):
        result = generate_test_sh("test", "dotnet", "perf", [], [], "easy")
        assert "source /app/assertion_runner.sh" in result

    def test_writes_eval_json(self):
        result = generate_test_sh("test", "dotnet", "perf", [], [], "easy")
        assert "eval.json" in result

    def test_writes_custom_metrics(self):
        result = generate_test_sh("test", "dotnet", "perf", [], [], "easy")
        assert "write_eval.py" in result

    def test_skill_and_plugin_in_metrics(self):
        result = generate_test_sh("test", "dotnet", "perf", [], [], "easy")
        assert '--skill "perf"' in result
        assert '--plugin "dotnet"' in result

    def test_multiple_assertions(self):
        assertions = [
            {"type": "output_contains", "value": "Compiled"},
            {"type": "output_contains", "value": "ToLower"},
            {"type": "exit_success"},
        ]
        result = generate_test_sh("test", "dotnet", "perf", assertions, [], "hard")
        assert "assert_output_contains 'Compiled'" in result
        assert "assert_output_contains 'ToLower'" in result
        assert "assert_exit_success" in result


# ============================================================================
# generate_solve_sh tests
# ============================================================================


class TestGenerateSolveSh:
    def test_solve_sh_output_contains(self):
        assertions = [{"type": "output_contains", "value": "performance"}]
        result = generate_solve_sh("dotnet--perf--test-task", assertions)
        assert "agent_output.txt" in result
        assert "performance" in result
        assert "exit 0" in result

    def test_solve_sh_empty_assertions(self):
        result = generate_solve_sh("dotnet--perf--test-task", [])
        assert "exit 0" in result
        assert "dotnet--perf--test-task" in result


# ============================================================================
# Task naming tests
# ============================================================================


class TestTaskNaming:
    def test_task_naming_format(self):
        """Test the {plugin}--{skill}--{slug} format."""
        tests = [
            ("dotnet", "csharp-scripts", "Test C# Script", "dotnet--csharp-scripts--c-script"),
            ("dotnet", "analyzing-dotnet-performance", "Detects compiled regex",
             "dotnet--analyzing-dotnet-performance--compiled-regex"),
            ("dotnet-msbuild", "msbuild-modernization", "Modernize legacy to SDK",
             "dotnet-msbuild--msbuild-modernization--legacy-sdk"),
        ]
        for plugin, skill, name, expected_prefix in tests:
            slug = slugify(name)
            task_name = f"{plugin}--{skill}--{slug}"
            assert task_name.startswith(expected_prefix.split("--")[0] + "--"), \
                f"Task name {task_name} doesn't have correct plugin prefix"
            assert "--" in task_name


# ============================================================================
# resolve_fixture_path tests
# ============================================================================


class TestResolveFixturePath:
    def test_relative_path_resolution(self):
        """Test that fixture paths resolve correctly."""
        with tempfile.TemporaryDirectory() as tmpdir:
            tmpdir = Path(tmpdir)
            # Create a fixture file
            fixture_dir = tmpdir / "tests" / "dotnet" / "perf" / "fixtures"
            fixture_dir.mkdir(parents=True)
            fixture_file = fixture_dir / "test.cs"
            fixture_file.write_text("// test")

            eval_yaml_dir = tmpdir / "tests" / "dotnet" / "perf"
            source = "fixtures/test.cs"

            result = resolve_fixture_path(source, eval_yaml_dir, tmpdir)
            assert result.exists()
            assert result.name == "test.cs"

    def test_relative_path_with_dotdot(self):
        """Test that ../../../../ paths resolve correctly by stripping the prefix."""
        with tempfile.TemporaryDirectory() as tmpdir:
            tmpdir = Path(tmpdir)
            fixture_dir = tmpdir / "tests" / "dotnet" / "perf" / "fixtures"
            fixture_dir.mkdir(parents=True)
            fixture = fixture_dir / "file.cs"
            fixture.write_text("// test")

            eval_yaml_dir = tmpdir / "plugins" / "dotnet" / "skills" / "perf"
            eval_yaml_dir.mkdir(parents=True)

            source = "../../../../tests/dotnet/perf/fixtures/file.cs"
            result = resolve_fixture_path(source, eval_yaml_dir, tmpdir)
            assert result.exists()


# ============================================================================
# process_scenario end-to-end tests
# ============================================================================


class TestProcessScenario:
    def setup_method(self):
        self.tmpdir = Path(tempfile.mkdtemp())
        # Create repo structure
        (self.tmpdir / "msbench" / "shared" / "eval_helpers").mkdir(parents=True)
        (self.tmpdir / "msbench" / "shared" / "eval_helpers" / "assertion_runner.sh").write_text("#!/bin/bash")
        (self.tmpdir / "msbench" / "shared" / "eval_helpers" / "write_eval.py").write_text("# eval writer")

    def teardown_method(self):
        shutil.rmtree(self.tmpdir, ignore_errors=True)

    def test_single_scenario_generates_all_files(self):
        eval_yaml_dir = self.tmpdir / "tests" / "dotnet" / "perf"
        eval_yaml_dir.mkdir(parents=True)
        output_dir = self.tmpdir / "output"

        scenario = {
            "name": "Test scenario",
            "prompt": "Analyze this code",
            "assertions": [
                {"type": "output_contains", "value": "performance"},
                {"type": "exit_success"},
            ],
            "rubric": ["Good analysis"],
            "timeout": 120,
        }

        result = process_scenario(
            plugin="dotnet",
            skill="analyzing-dotnet-performance",
            scenario=scenario,
            eval_yaml_dir=eval_yaml_dir,
            test_dir=eval_yaml_dir,
            repo_root=self.tmpdir,
            output_dir=output_dir,
        )

        task_dir = output_dir / result["task_name"]
        assert task_dir.exists()
        assert (task_dir / "task.toml").exists()
        assert (task_dir / "instruction.md").exists()
        assert (task_dir / "environment" / "Dockerfile").exists()
        assert (task_dir / "tests" / "test.sh").exists()
        assert (task_dir / "solution" / "solve.sh").exists()
        assert (task_dir / "environment" / "eval_helpers").exists()

    def test_dry_run_creates_nothing(self):
        eval_yaml_dir = self.tmpdir / "tests" / "dotnet" / "perf"
        eval_yaml_dir.mkdir(parents=True)
        output_dir = self.tmpdir / "output"

        scenario = {
            "name": "Dry run test",
            "prompt": "Test prompt",
            "assertions": [{"type": "exit_success"}],
        }

        result = process_scenario(
            plugin="dotnet",
            skill="perf",
            scenario=scenario,
            eval_yaml_dir=eval_yaml_dir,
            test_dir=eval_yaml_dir,
            repo_root=self.tmpdir,
            output_dir=output_dir,
            dry_run=True,
        )

        assert not output_dir.exists()
        assert "task_name" in result

    def test_fixture_files_copied(self):
        # Create fixture
        eval_yaml_dir = self.tmpdir / "tests" / "dotnet" / "perf"
        (eval_yaml_dir / "fixtures").mkdir(parents=True)
        (eval_yaml_dir / "fixtures" / "test.cs").write_text("// fixture")
        output_dir = self.tmpdir / "output"

        scenario = {
            "name": "Fixture test",
            "prompt": "Analyze file",
            "setup": {
                "files": [
                    {"path": "Test.cs", "source": "fixtures/test.cs"}
                ]
            },
            "assertions": [{"type": "exit_success"}],
        }

        result = process_scenario(
            plugin="dotnet",
            skill="perf",
            scenario=scenario,
            eval_yaml_dir=eval_yaml_dir,
            test_dir=eval_yaml_dir,
            repo_root=self.tmpdir,
            output_dir=output_dir,
        )

        task_dir = output_dir / result["task_name"]
        fixture = task_dir / "environment" / "fixtures" / "Test.cs"
        assert fixture.exists()
        assert fixture.read_text() == "// fixture"

    def test_copy_test_files_scenario(self):
        # Create test files
        test_dir = self.tmpdir / "tests" / "dotnet-msbuild" / "modernization"
        test_dir.mkdir(parents=True)
        (test_dir / "eval.yaml").write_text("scenarios: []")
        (test_dir / "Project.csproj").write_text("<Project/>")
        (test_dir / "Program.cs").write_text("class P {}")
        output_dir = self.tmpdir / "output"

        scenario = {
            "name": "Copy test files",
            "prompt": "Modernize this project",
            "setup": {"copy_test_files": True},
            "assertions": [{"type": "output_contains", "value": "SDK"}],
        }

        result = process_scenario(
            plugin="dotnet-msbuild",
            skill="modernization",
            scenario=scenario,
            eval_yaml_dir=test_dir,
            test_dir=test_dir,
            repo_root=self.tmpdir,
            output_dir=output_dir,
        )

        task_dir = output_dir / result["task_name"]
        assert (task_dir / "environment" / "test_files" / "Project.csproj").exists()
        assert (task_dir / "environment" / "test_files" / "Program.cs").exists()
        # eval.yaml should NOT be copied
        assert not (task_dir / "environment" / "test_files" / "eval.yaml").exists()


# ============================================================================
# Integration: discover_skills tests
# ============================================================================


class TestDiscoverSkills:
    def setup_method(self):
        self.tmpdir = Path(tempfile.mkdtemp())

    def teardown_method(self):
        shutil.rmtree(self.tmpdir, ignore_errors=True)

    def _create_skill(self, plugin, skill, with_eval=True, scenarios=None,
                       msbench_ready=True):
        """Helper: create a skill directory structure."""
        skill_dir = self.tmpdir / "plugins" / plugin / "skills" / skill
        skill_dir.mkdir(parents=True, exist_ok=True)
        (skill_dir / "SKILL.md").write_text(f"# {skill}")

        if with_eval:
            test_dir = self.tmpdir / "tests" / plugin / skill
            test_dir.mkdir(parents=True, exist_ok=True)
            eval_content = {
                "msbench_ready": msbench_ready,
                "scenarios": scenarios or [
                    {"name": f"Test {skill}", "prompt": "test", "assertions": []}
                ]
            }
            (test_dir / "eval.yaml").write_text(yaml.dump(eval_content))

    def test_discovers_all_in_scope_skills(self):
        # Create in-scope skills
        self._create_skill("dotnet", "analyzing-dotnet-performance")
        self._create_skill("dotnet", "csharp-scripts")
        self._create_skill("dotnet-msbuild", "msbuild-modernization")

        # Create excluded skill
        self._create_skill("dotnet", "dump-collect")

        skills_dir = self.tmpdir / "plugins"
        tests_dir = self.tmpdir / "tests"
        skills = discover_skills(skills_dir, tests_dir, self.tmpdir)

        skill_names = [(p, s) for p, s, _, _ in skills]
        assert ("dotnet", "analyzing-dotnet-performance") in skill_names
        assert ("dotnet", "csharp-scripts") in skill_names
        assert ("dotnet-msbuild", "msbuild-modernization") in skill_names
        assert ("dotnet", "dump-collect") not in skill_names

    def test_excludes_mcp_dependent_skills(self):
        for skill in EXCLUDED_SKILLS:
            self._create_skill("dotnet-msbuild", skill)
        self._create_skill("dotnet-msbuild", "incremental-build")

        skills_dir = self.tmpdir / "plugins"
        tests_dir = self.tmpdir / "tests"
        skills = discover_skills(skills_dir, tests_dir, self.tmpdir)

        skill_names = [s for _, s, _, _ in skills]
        assert "incremental-build" in skill_names
        for excluded in EXCLUDED_SKILLS:
            assert excluded not in skill_names

    def test_skips_skills_without_eval_yaml(self):
        skill_dir = self.tmpdir / "plugins" / "dotnet" / "skills" / "no-eval"
        skill_dir.mkdir(parents=True)
        (skill_dir / "SKILL.md").write_text("# No eval")

        skills_dir = self.tmpdir / "plugins"
        tests_dir = self.tmpdir / "tests"
        skills = discover_skills(skills_dir, tests_dir, self.tmpdir)

        skill_names = [s for _, s, _, _ in skills]
        assert "no-eval" not in skill_names


# ============================================================================
# Integration: convert_all tests
# ============================================================================


class TestConvertAll:
    def setup_method(self):
        self.tmpdir = Path(tempfile.mkdtemp())
        # Create eval_helpers
        helpers_dir = self.tmpdir / "msbench" / "shared" / "eval_helpers"
        helpers_dir.mkdir(parents=True)
        (helpers_dir / "assertion_runner.sh").write_text("#!/bin/bash")
        (helpers_dir / "write_eval.py").write_text("# writer")

    def teardown_method(self):
        shutil.rmtree(self.tmpdir, ignore_errors=True)

    def test_multi_scenario_generates_multiple_tasks(self):
        # Create a skill with multiple scenarios (msbench_ready: true)
        skill_dir = self.tmpdir / "plugins" / "dotnet" / "skills" / "perf"
        skill_dir.mkdir(parents=True)
        (skill_dir / "SKILL.md").write_text("# perf")

        test_dir = self.tmpdir / "tests" / "dotnet" / "perf"
        test_dir.mkdir(parents=True)

        scenarios = []
        for i in range(5):
            scenarios.append({
                "name": f"Test scenario {i}",
                "prompt": f"Analyze issue {i}",
                "assertions": [{"type": "output_contains", "value": f"result{i}"}],
                "timeout": 60,
            })

        eval_content = {"msbench_ready": True, "scenarios": scenarios}
        (test_dir / "eval.yaml").write_text(yaml.dump(eval_content))

        output_dir = self.tmpdir / "output"
        results = convert_all(
            skills_dir=self.tmpdir / "plugins",
            tests_dir=self.tmpdir / "tests",
            output_dir=output_dir,
            repo_root=self.tmpdir,
        )

        assert len(results) == 5
        task_dirs = [d for d in output_dir.iterdir() if d.is_dir()]
        assert len(task_dirs) == 5

    def test_skips_evals_without_msbench_ready(self):
        """Evals without msbench_ready: true are skipped."""
        skill_dir = self.tmpdir / "plugins" / "dotnet" / "skills" / "perf"
        skill_dir.mkdir(parents=True)
        (skill_dir / "SKILL.md").write_text("# perf")

        test_dir = self.tmpdir / "tests" / "dotnet" / "perf"
        test_dir.mkdir(parents=True)

        eval_content = {
            "scenarios": [
                {"name": "Test", "prompt": "test",
                 "assertions": [{"type": "output_contains", "value": "x"}]}
            ]
        }
        (test_dir / "eval.yaml").write_text(yaml.dump(eval_content))

        output_dir = self.tmpdir / "output"
        results = convert_all(
            skills_dir=self.tmpdir / "plugins",
            tests_dir=self.tmpdir / "tests",
            output_dir=output_dir,
            repo_root=self.tmpdir,
        )

        assert len(results) == 0

    def test_converts_evals_with_msbench_ready(self):
        """Evals with msbench_ready: true are converted."""
        skill_dir = self.tmpdir / "plugins" / "dotnet" / "skills" / "perf"
        skill_dir.mkdir(parents=True)
        (skill_dir / "SKILL.md").write_text("# perf")

        test_dir = self.tmpdir / "tests" / "dotnet" / "perf"
        test_dir.mkdir(parents=True)

        eval_content = {
            "msbench_ready": True,
            "scenarios": [
                {"name": "Test", "prompt": "test",
                 "assertions": [{"type": "output_contains", "value": "x"}]}
            ]
        }
        (test_dir / "eval.yaml").write_text(yaml.dump(eval_content))

        output_dir = self.tmpdir / "output"
        results = convert_all(
            skills_dir=self.tmpdir / "plugins",
            tests_dir=self.tmpdir / "tests",
            output_dir=output_dir,
            repo_root=self.tmpdir,
        )

        assert len(results) == 1

    def test_check_mode_detects_drift(self, capsys):
        # Create a skill
        skill_dir = self.tmpdir / "plugins" / "dotnet" / "skills" / "perf"
        skill_dir.mkdir(parents=True)
        (skill_dir / "SKILL.md").write_text("# perf")

        test_dir = self.tmpdir / "tests" / "dotnet" / "perf"
        test_dir.mkdir(parents=True)

        eval_content = {
            "msbench_ready": True,
            "scenarios": [
                {"name": "Test", "prompt": "test", "assertions": []}
            ]
        }
        (test_dir / "eval.yaml").write_text(yaml.dump(eval_content))

        output_dir = self.tmpdir / "output"
        output_dir.mkdir(parents=True)
        # Create an "extra" task that shouldn't exist
        (output_dir / "old-stale-task").mkdir()

        results = convert_all(
            skills_dir=self.tmpdir / "plugins",
            tests_dir=self.tmpdir / "tests",
            output_dir=output_dir,
            repo_root=self.tmpdir,
            dry_run=True,
            check=True,
        )

        captured = capsys.readouterr()
        assert "DRIFT" in captured.out


# ============================================================================
# Excluded skills validation
# ============================================================================


class TestExcludedSkills:
    def test_excluded_skills_list(self):
        """Verify all expected skills are excluded."""
        expected = {
            "binlog-failure-analysis",
            "binlog-generation",
            "build-perf-diagnostics",
            "build-parallelism",
            "dump-collect",
        }
        assert EXCLUDED_SKILLS == expected


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
