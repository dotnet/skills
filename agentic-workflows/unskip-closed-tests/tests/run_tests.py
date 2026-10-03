import hashlib
import json
import os
import shutil
import subprocess
import sys
import unittest
from pathlib import Path


TESTS_DIR = Path(__file__).resolve().parent
PACKAGE_DIR = TESTS_DIR.parent
TOOL_DIR = PACKAGE_DIR / "workflows" / "unskip-closed-tests-tool"
TOOL_PROJECT = TOOL_DIR / "UnskipClosedTests.Tool.csproj"
TOOL_DLL = TOOL_DIR / "bin" / "Debug" / "net8.0" / "UnskipClosedTests.Tool.dll"
HOOK = TESTS_DIR / "fixtures" / "verification_hook.py"
WORK_PARENT = TESTS_DIR / ".work"
WORK_DIR = WORK_PARENT / str(os.getpid())


def run(command, cwd=None, expected=0):
    completed = subprocess.run(
        [str(item) for item in command],
        cwd=cwd,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )
    if completed.returncode != expected:
        raise AssertionError(
            f"command returned {completed.returncode}, expected {expected}\n"
            f"command: {command}\nstdout:\n{completed.stdout}\nstderr:\n{completed.stderr}"
        )
    return completed


def canonical_digest(document):
    value = dict(document)
    value.pop("manifest_digest", None)
    encoded = json.dumps(
        value, sort_keys=True, separators=(",", ":"), ensure_ascii=False
    ).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


class FixtureRepo:
    def __init__(self, name, source):
        self.root = WORK_DIR / name
        shutil.rmtree(self.root, ignore_errors=True)
        (self.root / "src").mkdir(parents=True)
        (self.root / "src" / "Tests.cs").write_text(source, encoding="utf-8")
        run(["git", "init", "--quiet"], self.root)
        run(["git", "config", "user.email", "tests@example.invalid"], self.root)
        run(["git", "config", "user.name", "Fixture Tests"], self.root)
        run(["git", "add", "."], self.root)
        run(["git", "commit", "--quiet", "-m", "fixture"], self.root)
        self.config = self.root / "config.json"
        self.write_json(
            self.config,
            {
                "schema_version": "1",
                "source_roots": ["src"],
                "excluded_globs": [],
                "generated_globs": ["**/*.g.cs"],
                "ignore_attribute_names": ["Demo.IgnoreAttribute"],
                "test_attribute_names": ["Demo.TestAttribute"],
                "verification": {
                    "command": [sys.executable, str(HOOK)],
                    "timeout_seconds": 30,
                },
            },
        )

    @staticmethod
    def write_json(path, value):
        path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")

    def tool(self, *arguments, expected=0):
        return run(
            [
                "dotnet",
                str(TOOL_DLL),
                *arguments,
                "--repo-root",
                str(self.root),
            ]
            if arguments[0] in ("inventory", "apply", "materialize")
            else ["dotnet", str(TOOL_DLL), *arguments],
            cwd=self.root,
            expected=expected,
        )

    def inventory(self, expected=0, source_commit=None):
        output = self.root / "inventory.json"
        output.unlink(missing_ok=True)
        arguments = [
            "inventory",
            "--config",
            str(self.config),
            "--output",
            str(output),
            "--repository",
            "fixture/repo",
        ]
        if source_commit is not None:
            arguments.extend(["--source-commit", source_commit])
        self.tool(*arguments, expected=expected)
        return json.loads(output.read_text(encoding="utf-8")) if output.exists() else None

    def evidence(self, entries):
        path = self.root / "evidence.json"
        self.write_json(
            path,
            {
                "schema_version": "1",
                "references": entries,
            },
        )
        return path

    def resolve(self, manifest, evidence, expected=0):
        manifest_path = self.root / "manifest-input.json"
        output = self.root / "resolved.json"
        self.write_json(manifest_path, manifest)
        self.tool(
            "resolve",
            "--manifest",
            str(manifest_path),
            "--github-evidence",
            str(evidence),
            "--output",
            str(output),
            expected=expected,
        )
        return json.loads(output.read_text(encoding="utf-8")) if output.exists() else None

    def apply(
        self,
        manifest,
        evidence,
        candidate_ids,
        expected=0,
        extra_item=None,
        evidence_dir=None,
    ):
        manifest_path = self.root / "resolved-input.json"
        agent_path = self.root / "agent-output.json"
        output = self.root / "apply-result.json"
        self.write_json(manifest_path, manifest)
        item = {
            "type": "apply_verified_unskips",
            "manifest_digest": manifest["manifest_digest"],
            "candidate_ids_json": candidate_ids,
        }
        if extra_item:
            item.update(extra_item)
        self.write_json(agent_path, {"items": [item]})
        arguments = [
            "apply",
            "--config",
            str(self.config),
            "--manifest",
            str(manifest_path),
            "--agent-output",
            str(agent_path),
            "--github-evidence",
            str(evidence),
            "--output",
            str(output),
        ]
        if evidence_dir is not None:
            arguments.extend(["--evidence-dir", str(evidence_dir)])
        self.tool(*arguments, expected=expected)
        return json.loads(output.read_text(encoding="utf-8")) if output.exists() else None

    def authorize(self, manifest, evidence, evidence_dir, expected=0):
        manifest_path = self.root / "authorize-manifest.json"
        output = self.root / "authorize-result.json"
        self.write_json(manifest_path, manifest)
        self.tool(
            "authorize",
            "--config",
            str(self.config),
            "--manifest",
            str(manifest_path),
            "--agent-output",
            str(evidence_dir / "agent-output.json"),
            "--evidence-dir",
            str(evidence_dir),
            "--github-evidence",
            str(evidence),
            "--output",
            str(output),
            expected=expected,
        )
        return json.loads(output.read_text(encoding="utf-8")) if output.exists() else None

    def materialize(self, manifest, evidence, result, expected=0):
        manifest_path = self.root / "materialize-manifest.json"
        result_path = self.root / "materialize-result.json"
        self.write_json(manifest_path, manifest)
        self.write_json(result_path, result)
        self.tool(
            "materialize",
            "--config",
            str(self.config),
            "--manifest",
            str(manifest_path),
            "--result",
            str(result_path),
            "--github-evidence",
            str(evidence),
            expected=expected,
        )


class TrustedHelperRegressionTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        shutil.rmtree(WORK_DIR, ignore_errors=True)
        WORK_DIR.mkdir(parents=True, exist_ok=True)
        run(
            [
                "dotnet",
                "build",
                str(TOOL_PROJECT),
                "--nologo",
                "--verbosity:minimal",
            ],
            cwd=TOOL_DIR,
        )

    @classmethod
    def tearDownClass(cls):
        shutil.rmtree(WORK_DIR, ignore_errors=True)
        try:
            WORK_PARENT.rmdir()
        except OSError:
            pass

    def test_identical_ignore_text_has_distinct_site_identity(self):
        repo = FixtureRepo(
            self.id().split(".")[-1],
            """
namespace Demo;
public class Tests
{
    [Ignore("#1")]
    [Ignore("#1")]
    [Test]
    public void SameOwner() { }
}
""".lstrip(),
        )
        manifest = repo.inventory()
        self.assertEqual(2, manifest["candidate_count"])
        first, second = manifest["candidates"]
        self.assertEqual(first["stable_owner_id"], second["stable_owner_id"])
        self.assertNotEqual(first["candidate_id"], second["candidate_id"])
        self.assertNotEqual(first["attribute_span"]["start"], second["attribute_span"]["start"])

    def test_stable_owner_identity_survives_line_movement(self):
        repo = FixtureRepo(
            self.id().split(".")[-1],
            """
namespace Demo;
public class Tests
{
    [Ignore("#1")]
    [Test]
    public void RealTest() { }
}
""".lstrip(),
        )
        before = repo.inventory()["candidates"][0]
        source = repo.root / "src" / "Tests.cs"
        source.write_text("\n\n" + source.read_text(encoding="utf-8"), encoding="utf-8")
        run(["git", "add", "src/Tests.cs"], repo.root)
        run(["git", "commit", "--quiet", "-m", "move lines"], repo.root)

        after = repo.inventory()["candidates"][0]
        self.assertEqual(before["stable_owner_id"], after["stable_owner_id"])
        self.assertNotEqual(before["candidate_id"], after["candidate_id"])
        self.assertNotEqual(before["blob_oid"], after["blob_oid"])
        self.assertNotEqual(
            before["attribute_span"]["start"], after["attribute_span"]["start"]
        )

    def test_distinct_declarations_never_share_stable_owner_identity(self):
        repo = FixtureRepo(
            self.id().split(".")[-1],
            """
namespace Demo;
[Ignore("#1")]
public class Duplicate { [Test] public void First() { } }
[Ignore("#1")]
public class Duplicate { [Test] public void Second() { } }
""".lstrip(),
        )
        manifest = repo.inventory()
        self.assertEqual(2, manifest["candidate_count"])
        self.assertEqual(
            2,
            len(
                {
                    candidate["stable_owner_id"]
                    for candidate in manifest["candidates"]
                }
            ),
        )

    def test_missing_source_roots_are_a_successful_empty_inventory(self):
        repo = FixtureRepo(
            self.id().split(".")[-1],
            """
namespace Demo;
public class Tests { }
""".lstrip(),
        )
        config = json.loads(repo.config.read_text(encoding="utf-8"))
        config["source_roots"] = ["missing-tests"]
        repo.write_json(repo.config, config)

        manifest = repo.inventory()
        self.assertEqual(0, manifest["candidate_count"])
        self.assertEqual([], manifest["candidates"])

    def test_inventory_source_commit_and_help_contract(self):
        repo = FixtureRepo(
            self.id().split(".")[-1],
            """
namespace Demo;
public class Tests { }
""".lstrip(),
        )
        source_commit = run(["git", "rev-parse", "HEAD"], repo.root).stdout.strip()
        manifest = repo.inventory(source_commit=source_commit)
        self.assertEqual(source_commit, manifest["source_commit"])
        self.assertIsNone(repo.inventory(expected=20, source_commit="0" * 40))

        help_result = run(["dotnet", str(TOOL_DLL), "--help"], cwd=repo.root)
        self.assertIn(
            "inventory --config <path> --output <path>", help_result.stdout
        )
        self.assertIn("--source-commit <sha>", help_result.stdout)
        self.assertIn(
            "10 Apply retained no verified candidates", help_result.stdout
        )

    def test_verification_config_requires_command_argv(self):
        repo = FixtureRepo(
            self.id().split(".")[-1],
            """
namespace Demo;
public class Tests { }
""".lstrip(),
        )
        config = json.loads(repo.config.read_text(encoding="utf-8"))
        config["verification"]["argv"] = config["verification"].pop("command")
        repo.write_json(repo.config, config)
        repo.inventory(expected=20)

    def test_generated_source_and_declarations_never_become_eligible(self):
        header_repo = FixtureRepo(
            self.id().split(".")[-1] + "_header",
            """
// <auto-generated/>
namespace Demo;
public class Tests
{
    [Ignore("#1")][Test] public void Generated() { }
}
""".lstrip(),
        )
        self.assertEqual(0, header_repo.inventory()["candidate_count"])

        compact_header_repo = FixtureRepo(
            self.id().split(".")[-1] + "_compact_header",
            """
// <autogenerated/>
namespace Demo;
public class Tests
{
    [Ignore("#1")][Test] public void Generated() { }
}
""".lstrip(),
        )
        self.assertEqual(
            0, compact_header_repo.inventory()["candidate_count"]
        )

        declaration_repo = FixtureRepo(
            self.id().split(".")[-1] + "_declaration",
            """
namespace Demo;
[System.CodeDom.Compiler.GeneratedCodeAttribute]
public class GeneratedTests
{
    [Ignore("#1")]
    [Test]
    public void GeneratedByContainingType() { }
}
public class Tests
{
    [System.CodeDom.Compiler.GeneratedCodeAttribute]
    [Ignore("#1")]
    [Test]
    public void GeneratedCodeSuffix() { }

    [System.Runtime.CompilerServices.CompilerGeneratedAttribute]
    [Ignore("#1")]
    [Test]
    public void CompilerGeneratedSuffix() { }
}
""".lstrip(),
        )
        evidence = declaration_repo.evidence(
            {
                "fixture/repo#1": {
                    "kind": "issue",
                    "state": "closed",
                    "state_reason": "completed",
                }
            }
        )
        resolved = declaration_repo.resolve(declaration_repo.inventory(), evidence)
        self.assertEqual(3, resolved["candidate_count"])
        self.assertTrue(
            all(
                not candidate["decision"]["eligible"]
                and "generated_declaration" in candidate["decision"]["deferrals"]
                for candidate in resolved["candidates"]
            )
        )

    def test_attribute_identity_is_framework_qualified_and_alias_aware(self):
        custom_repo = FixtureRepo(
            self.id().split(".")[-1] + "_custom",
            """
using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace Demo;
public sealed class IgnoreAttribute : System.Attribute
{
    public IgnoreAttribute(string message) { }
}
public class Tests
{
    [Ignore("#1")]
    [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
    public void CustomIgnore() { }
}
""".lstrip(),
        )
        config = json.loads(custom_repo.config.read_text(encoding="utf-8"))
        config["ignore_attribute_names"] = [
            "Microsoft.VisualStudio.TestTools.UnitTesting.IgnoreAttribute"
        ]
        config["test_attribute_names"] = [
            "Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute"
        ]
        custom_repo.write_json(custom_repo.config, config)
        self.assertEqual(0, custom_repo.inventory()["candidate_count"])

        alias_repo = FixtureRepo(
            self.id().split(".")[-1] + "_alias",
            """
using Skip = Microsoft.VisualStudio.TestTools.UnitTesting.IgnoreAttribute;
using MSTest = Microsoft.VisualStudio.TestTools.UnitTesting;
namespace Demo;
public class Tests
{
    [Skip("#1")]
    [MSTest.TestMethod]
    public void AliasedIgnore() { }
}
""".lstrip(),
        )
        alias_repo.write_json(alias_repo.config, config)
        manifest = alias_repo.inventory()
        self.assertEqual(1, manifest["candidate_count"])
        self.assertEqual(
            "Microsoft.VisualStudio.TestTools.UnitTesting.IgnoreAttribute",
            manifest["candidates"][0]["attribute_type"],
        )

    def test_fabricated_agent_anchor_or_fqn_is_rejected(self):
        repo = FixtureRepo(
            self.id().split(".")[-1],
            """
namespace Demo;
public class Tests
{
    [Ignore("#1")]
    [Test]
    public void RealTest() { }
}
""".lstrip(),
        )
        manifest = repo.inventory()
        evidence = repo.evidence(
            {
                "fixture/repo#1": {
                    "kind": "issue",
                    "state": "closed",
                    "state_reason": "completed",
                }
            }
        )
        resolved = repo.resolve(manifest, evidence)
        original = (repo.root / "src" / "Tests.cs").read_bytes()
        repo.apply(
            resolved,
            evidence,
            [resolved["candidates"][0]["candidate_id"]],
            expected=20,
            extra_item={"test_fqns": ["Fabricated.Type.Test"]},
        )
        self.assertEqual(original, (repo.root / "src" / "Tests.cs").read_bytes())

    def test_false_nested_type_chain_is_rejected_after_valid_digest(self):
        repo = FixtureRepo(
            self.id().split(".")[-1],
            """
namespace Demo;
public class Outer
{
    public class Actual
    {
        [Ignore("#1")]
        [Test]
        public void RealTest() { }
    }
}
""".lstrip(),
        )
        evidence = repo.evidence(
            {
                "fixture/repo#1": {
                    "kind": "issue",
                    "state": "closed",
                    "state_reason": "completed",
                }
            }
        )
        resolved = repo.resolve(repo.inventory(), evidence)
        resolved["candidates"][0]["owner"]["containing_types"] = [
            "Outer",
            "Fabricated",
            "Actual",
        ]
        resolved["manifest_digest"] = canonical_digest(resolved)
        original = (repo.root / "src" / "Tests.cs").read_bytes()
        repo.apply(
            resolved,
            evidence,
            [resolved["candidates"][0]["candidate_id"]],
            expected=20,
        )
        self.assertEqual(original, (repo.root / "src" / "Tests.cs").read_bytes())

    def test_fabricated_attribute_anchor_is_rejected_after_valid_digest(self):
        repo = FixtureRepo(
            self.id().split(".")[-1],
            """
namespace Demo;
public class Tests
{
    [Ignore("#1")]
    [Test]
    public void RealTest() { }
}
""".lstrip(),
        )
        evidence = repo.evidence(
            {
                "fixture/repo#1": {
                    "kind": "issue",
                    "state": "closed",
                    "state_reason": "completed",
                }
            }
        )
        resolved = repo.resolve(repo.inventory(), evidence)
        resolved["candidates"][0]["attribute_span"]["start"] += 1
        resolved["manifest_digest"] = canonical_digest(resolved)
        original = (repo.root / "src" / "Tests.cs").read_bytes()
        repo.apply(
            resolved,
            evidence,
            [resolved["candidates"][0]["candidate_id"]],
            expected=20,
        )
        self.assertEqual(original, (repo.root / "src" / "Tests.cs").read_bytes())

    def test_class_level_ambiguity_is_deferred(self):
        repo = FixtureRepo(
            self.id().split(".")[-1],
            """
namespace Demo;
public class Outer
{
    [Ignore("#1")]
    public class Nested { [Test] public void A() { } }
}
[Ignore("#1")]
public partial class Partial { [Test] public void B() { } }
public partial class Partial { }
public class Base { }
[Ignore("#1")]
public class Derived : Base { [Test] public void C() { } }
[Ignore("#1")]
public class Duplicate { [Test] public void D() { } }
public class Duplicate { }
""".lstrip(),
        )
        evidence = repo.evidence(
            {
                "fixture/repo#1": {
                    "kind": "issue",
                    "state": "closed",
                    "state_reason": "completed",
                }
            }
        )
        resolved = repo.resolve(repo.inventory(), evidence)
        reasons = {
            reason
            for candidate in resolved["candidates"]
            for reason in candidate["decision"]["deferrals"]
        }
        self.assertIn("class_is_nested", reasons)
        self.assertIn("class_is_partial", reasons)
        self.assertIn("class_has_base_types", reasons)
        self.assertIn("duplicate_type_declarations", reasons)
        self.assertTrue(all(not c["decision"]["eligible"] for c in resolved["candidates"]))

    def test_stale_source_revision_is_rejected_without_tool_changes(self):
        repo = FixtureRepo(
            self.id().split(".")[-1],
            """
namespace Demo;
public class Tests
{
    [Ignore("#1")]
    [Test]
    public void RealTest() { }
}
""".lstrip(),
        )
        evidence = repo.evidence(
            {
                "fixture/repo#1": {
                    "kind": "issue",
                    "state": "closed",
                    "state_reason": "completed",
                }
            }
        )
        resolved = repo.resolve(repo.inventory(), evidence)
        source = repo.root / "src" / "Tests.cs"
        source.write_text(source.read_text(encoding="utf-8") + "// stale\n", encoding="utf-8")
        stale = source.read_bytes()
        repo.apply(
            resolved,
            evidence,
            [resolved["candidates"][0]["candidate_id"]],
            expected=20,
        )
        self.assertEqual(stale, source.read_bytes())

    def test_malformed_github_evidence_is_rejected(self):
        repo = FixtureRepo(
            self.id().split(".")[-1],
            """
namespace Demo;
public class Tests
{
    [Ignore("#1 and #2")]
    [Test]
    public void RealTest() { }
}
""".lstrip(),
        )
        evidence = repo.evidence(
            {
                "fixture/repo#1": {
                    "kind": "issue",
                    "state": "closed",
                    "state_reason": "completed",
                },
                "fixture/repo#2": {"kind": "issue", "state": "closed"},
            }
        )
        repo.resolve(repo.inventory(), evidence, expected=20)

    def test_issue_state_reason_nullability_is_fail_closed(self):
        repo = FixtureRepo(
            self.id().split(".")[-1],
            """
namespace Demo;
public class Tests
{
    [Ignore("#1")][Test] public void StateReason() { }
}
""".lstrip(),
        )
        inventory = repo.inventory()

        open_issue = repo.resolve(
            inventory,
            repo.evidence(
                {
                    "fixture/repo#1": {
                        "kind": "issue",
                        "state": "open",
                        "state_reason": None,
                    }
                }
            ),
        )
        reference = open_issue["candidates"][0]["canonical_issue_references"][0]
        self.assertFalse(reference["eligibility"])
        self.assertEqual("open", reference["state_reason"])

        completed = repo.resolve(
            inventory,
            repo.evidence(
                {
                    "fixture/repo#1": {
                        "kind": "issue",
                        "state": "closed",
                        "state_reason": "completed",
                    }
                }
            ),
        )
        self.assertTrue(completed["candidates"][0]["decision"]["eligible"])

        not_planned = repo.resolve(
            inventory,
            repo.evidence(
                {
                    "fixture/repo#1": {
                        "kind": "issue",
                        "state": "closed",
                        "state_reason": "not_planned",
                    }
                }
            ),
        )
        self.assertFalse(not_planned["candidates"][0]["decision"]["eligible"])

        malformed = repo.evidence(
            {
                "fixture/repo#1": {
                    "kind": "issue",
                    "state": "open",
                    "state_reason": 42,
                }
            }
        )
        repo.resolve(inventory, malformed, expected=20)

    def test_reference_numbers_require_right_identifier_boundary(self):
        repo = FixtureRepo(
            self.id().split(".")[-1],
            """
namespace Demo;
public class Tests
{
    [Ignore("https://github.com/fixture/repo/issues/101, tracked")][Test]
    public void FullReference() { }
    [Ignore("other/repo#102. tracked")][Test]
    public void QualifiedReference() { }
    [Ignore("#103) tracked")][Test]
    public void BareReference() { }
    [Ignore("https://github.com/fixture/repo/issues/201abc")][Test]
    public void MalformedFullReference() { }
    [Ignore("other/repo#202abc")][Test]
    public void MalformedQualifiedReference() { }
    [Ignore("#203abc")][Test]
    public void MalformedBareReference() { }
}
""".lstrip(),
        )

        manifest = repo.inventory()
        references = {
            candidate["owner"]["method_name"]: [
                reference["canonical"]
                for reference in candidate["canonical_issue_references"]
            ]
            for candidate in manifest["candidates"]
        }
        self.assertEqual(
            {
                "FullReference": ["fixture/repo#101"],
                "QualifiedReference": ["other/repo#102"],
                "BareReference": ["fixture/repo#103"],
            },
            references,
        )

    def test_candidate_requires_every_reference_to_qualify(self):
        repo = FixtureRepo(
            self.id().split(".")[-1],
            """
namespace Demo;
public class Tests
{
    [Ignore("#1, fixture/repo#2, and https://github.com/fixture/repo/pull/3")]
    [Test]
    public void MultiReferenceTest() { }
}
""".lstrip(),
        )
        inventory = repo.inventory()
        base_entries = {
            "fixture/repo#1": {
                "kind": "issue",
                "state": "closed",
                "state_reason": "completed",
            },
            "fixture/repo#3": {
                "kind": "pull_request",
                "state": "closed",
                "merged_at": "2026-01-01T00:00:00Z",
            },
        }

        inaccessible = repo.resolve(inventory, repo.evidence(base_entries))
        self.assertFalse(inaccessible["candidates"][0]["decision"]["eligible"])
        self.assertIn(
            "inaccessible",
            {
                reference["state_reason"]
                for reference in inaccessible["candidates"][0][
                    "canonical_issue_references"
                ]
            },
        )

        for state, state_reason in (("open", "reopened"), ("closed", "not_planned")):
            entries = dict(base_entries)
            entries["fixture/repo#2"] = {
                "kind": "issue",
                "state": state,
                "state_reason": state_reason,
            }
            resolved = repo.resolve(inventory, repo.evidence(entries))
            self.assertFalse(resolved["candidates"][0]["decision"]["eligible"])

        completed_entries = dict(base_entries)
        completed_entries["fixture/repo#2"] = {
            "kind": "issue",
            "state": "closed",
            "state_reason": "completed",
        }
        eligible = repo.resolve(inventory, repo.evidence(completed_entries))
        candidate = eligible["candidates"][0]
        self.assertTrue(candidate["decision"]["eligible"])
        self.assertEqual(3, len(candidate["canonical_issue_references"]))
        self.assertTrue(
            all(
                reference["eligibility"]
                for reference in candidate["canonical_issue_references"]
            )
        )

    def test_inaccessible_not_planned_and_open_references_are_ineligible(self):
        repo = FixtureRepo(
            self.id().split(".")[-1],
            """
namespace Demo;
public class Tests
{
    [Ignore("#1")][Test] public void Inaccessible() { }
    [Ignore("#2")][Test] public void NotPlanned() { }
    [Ignore("#3")][Test] public void Open() { }
}
""".lstrip(),
        )
        evidence = repo.evidence(
            {
                "fixture/repo#1": {"kind": "issue", "accessible": False},
                "fixture/repo#2": {
                    "kind": "issue",
                    "state": "closed",
                    "state_reason": "not_planned",
                },
                "fixture/repo#3": {
                    "kind": "issue",
                    "state": "open",
                    "state_reason": "reopened",
                },
            }
        )
        resolved = repo.resolve(repo.inventory(), evidence)
        self.assertTrue(all(not c["decision"]["eligible"] for c in resolved["candidates"]))
        state_reasons = {
            reference["state_reason"]
            for candidate in resolved["candidates"]
            for reference in candidate["canonical_issue_references"]
        }
        self.assertEqual({"inaccessible", "not_planned", "open"}, state_reasons)

    def test_pull_request_qualifies_only_when_merged(self):
        repo = FixtureRepo(
            self.id().split(".")[-1],
            """
namespace Demo;
public class Tests
{
    [Ignore("https://github.com/fixture/repo/pull/4")][Test]
    public void PullRequestTracked() { }
}
""".lstrip(),
        )
        inventory = repo.inventory()
        unmerged = repo.evidence(
            {
                "fixture/repo#4": {
                    "kind": "pull_request",
                    "state": "closed",
                    "merged_at": None,
                }
            }
        )
        resolved = repo.resolve(inventory, unmerged)
        self.assertFalse(resolved["candidates"][0]["decision"]["eligible"])

        merged = repo.evidence(
            {
                "fixture/repo#4": {
                    "kind": "pull_request",
                    "state": "closed",
                    "merged_at": "2026-01-01T00:00:00Z",
                }
            }
        )
        resolved = repo.resolve(inventory, merged)
        self.assertTrue(resolved["candidates"][0]["decision"]["eligible"])

    def test_only_merged_pull_request_is_eligible(self):
        repo = FixtureRepo(
            self.id().split(".")[-1],
            """
namespace Demo;
public class Tests
{
    [Ignore("#4")][Test] public void MergedPull() { }
    [Ignore("#5")][Test] public void ClosedUnmergedPull() { }
}
""".lstrip(),
        )
        evidence = repo.evidence(
            {
                "fixture/repo#4": {
                    "kind": "pull_request",
                    "state": "closed",
                    "state_reason": "",
                    "merged_at": "2026-01-02T03:04:05Z",
                },
                "fixture/repo#5": {
                    "kind": "pull_request",
                    "state": "closed",
                    "state_reason": "",
                    "merged_at": None,
                },
            }
        )
        resolved = repo.resolve(repo.inventory(), evidence)
        decisions = {
            candidate["owner"]["method_name"]: candidate["decision"]["eligible"]
            for candidate in resolved["candidates"]
        }
        self.assertEqual(
            {"MergedPull": True, "ClosedUnmergedPull": False}, decisions
        )

    def test_trx_protocol_retains_only_exact_executed_pass(self):
        repo = FixtureRepo(
            self.id().split(".")[-1],
            """
namespace Demo;
public class Tests
{
    [Ignore("#1")][Test] public void ZeroSelected() { }
    [Ignore("#1")][Test] public void AllSkipped() { }
    [Ignore("#1")][Test] public void MismatchFqn() { }
    [Ignore("#1")][Test] public void ExecutedPass() { }
}
""".lstrip(),
        )
        evidence = repo.evidence(
            {
                "fixture/repo#1": {
                    "kind": "issue",
                    "state": "closed",
                    "state_reason": "completed",
                }
            }
        )
        resolved = repo.resolve(repo.inventory(), evidence)
        ids = [candidate["candidate_id"] for candidate in resolved["candidates"]]
        result = repo.apply(resolved, evidence, ids)
        self.assertEqual("1", result["schema_version"])
        self.assertEqual(resolved["source_commit"], result["source_commit"])
        self.assertEqual(1, len(result["retained_candidates"]))
        self.assertEqual(3, len(result["reverted_candidates"]))
        self.assertEqual(["src/Tests.cs"], result["changed_paths"])
        source = (repo.root / "src" / "Tests.cs").read_text(encoding="utf-8")
        self.assertEqual(3, source.count("[Ignore"))
        self.assertIn("[Test] public void ExecutedPass()", source)
        pass_candidate = next(
            candidate
            for candidate in resolved["candidates"]
            if candidate["owner"]["method_name"] == "ExecutedPass"
        )
        self.assertEqual(
            [pass_candidate["candidate_id"]],
            [candidate["candidate_id"] for candidate in result["retained_candidates"]],
        )
        self.assertEqual(
            ["src/Tests.cs"],
            [candidate["path"] for candidate in result["retained_candidates"]],
        )
        self.assertEqual(
            [pass_candidate["owner"]["test_fqns"]],
            [candidate["test_fqns"] for candidate in result["retained_candidates"]],
        )
        self.assertEqual(
            "[unskip-closed-tests] Unskip test for completed GitHub work item",
            result["pr_title"],
        )
        marker = (
            "<!-- unskip-closed-tests:v1;"
            f"source={resolved['source_commit']};"
            f"manifest={resolved['manifest_digest']} -->"
        )
        self.assertTrue(result["pr_body"].startswith(marker))
        self.assertNotIn("retained_candidate_ids", result)
        reasons = {item["reason"] for item in result["reverted_candidates"]}
        self.assertTrue(
            all(item["path"] == "src/Tests.cs" for item in result["reverted_candidates"])
        )
        self.assertTrue(
            all(item["test_fqns"] for item in result["reverted_candidates"])
        )
        self.assertIn("zero_selected_tests", reasons)
        self.assertIn("non_passing_outcome:NotExecuted", reasons)
        self.assertTrue(any(reason.startswith("mismatched_fqn:") for reason in reasons))

    def test_trx_requires_one_passing_result_per_definition(self):
        repo = FixtureRepo(
            self.id().split(".")[-1],
            """
namespace Demo;
public class Tests
{
    [Ignore("#1")][Test] public void PartialDefinitions() { }
    [Ignore("#1")][Test] public void DuplicateResult() { }
}
""".lstrip(),
        )
        evidence = repo.evidence(
            {
                "fixture/repo#1": {
                    "kind": "issue",
                    "state": "closed",
                    "state_reason": "completed",
                }
            }
        )
        resolved = repo.resolve(repo.inventory(), evidence)
        result = repo.apply(
            resolved,
            evidence,
            [candidate["candidate_id"] for candidate in resolved["candidates"]],
            expected=10,
        )
        reasons = {candidate["reason"] for candidate in result["reverted_candidates"]}
        self.assertTrue(any(reason.startswith("definition_without_result:") for reason in reasons))
        self.assertTrue(any(reason.startswith("duplicate_trx_result_id:") for reason in reasons))

    def test_authorization_is_derived_from_fresh_trx_evidence(self):
        repo = FixtureRepo(
            self.id().split(".")[-1],
            """
namespace Demo;
public class Tests
{
    [Ignore("#1")][Test] public void ExecutedPass() { }
}
""".lstrip(),
        )
        evidence = repo.evidence(
            {
                "fixture/repo#1": {
                    "kind": "issue",
                    "state": "closed",
                    "state_reason": "completed",
                }
            }
        )
        resolved = repo.resolve(repo.inventory(), evidence)
        candidate_id = resolved["candidates"][0]["candidate_id"]
        evidence_dir = repo.root / "verification-evidence"
        source = repo.root / "src" / "Tests.cs"
        original = source.read_bytes()
        repo.apply(
            resolved,
            evidence,
            [candidate_id],
            evidence_dir=evidence_dir,
        )
        source.write_bytes(original)

        authorized = repo.authorize(resolved, evidence, evidence_dir)
        self.assertEqual(
            [candidate_id],
            [candidate["candidate_id"] for candidate in authorized["retained_candidates"]],
        )

        trx = next((evidence_dir / "final" / candidate_id).glob("*.trx"))
        trx.write_text("<TestRun />", encoding="utf-8")
        rejected = repo.authorize(resolved, evidence, evidence_dir, expected=10)
        self.assertFalse(rejected["has_changes"])
        self.assertEqual([], rejected["retained_candidates"])

    def test_multiple_retained_tests_title_has_deduplication_marker(self):
        repo = FixtureRepo(
            self.id().split(".")[-1],
            """
namespace Demo;
public class Tests
{
    [Ignore("#1")][Test] public void ExecutedPassOne() { }
    [Ignore("#1")][Test] public void ExecutedPassTwo() { }
}
""".lstrip(),
        )
        evidence = repo.evidence(
            {
                "fixture/repo#1": {
                    "kind": "issue",
                    "state": "closed",
                    "state_reason": "completed",
                }
            }
        )
        resolved = repo.resolve(repo.inventory(), evidence)
        result = repo.apply(
            resolved,
            evidence,
            [candidate["candidate_id"] for candidate in resolved["candidates"]],
        )
        self.assertEqual(2, len(result["retained_candidates"]))
        self.assertEqual(
            "[unskip-closed-tests] Unskip 2 tests for completed GitHub work items",
            result["pr_title"],
        )

    def test_class_level_title_counts_affected_tests(self):
        repo = FixtureRepo(
            self.id().split(".")[-1],
            """
namespace Demo;
[Ignore("#1")]
public class Tests
{
    [Test] public void ExecutedPassOne() { }
    [Test] public void ExecutedPassTwo() { }
}
""".lstrip(),
        )
        evidence = repo.evidence(
            {
                "fixture/repo#1": {
                    "kind": "issue",
                    "state": "closed",
                    "state_reason": "completed",
                }
            }
        )
        resolved = repo.resolve(repo.inventory(), evidence)
        result = repo.apply(
            resolved,
            evidence,
            [resolved["candidates"][0]["candidate_id"]],
        )
        self.assertEqual(
            "[unskip-closed-tests] Unskip 2 tests for completed GitHub work items",
            result["pr_title"],
        )

    def test_verification_hook_does_not_receive_github_tokens(self):
        repo = FixtureRepo(
            self.id().split(".")[-1],
            """
namespace Demo;
public class Tests
{
    [Ignore("#1")][Test] public void ExecutedPass() { }
}
""".lstrip(),
        )
        evidence = repo.evidence(
            {
                "fixture/repo#1": {
                    "kind": "issue",
                    "state": "closed",
                    "state_reason": "completed",
                }
            }
        )
        config = json.loads(repo.config.read_text(encoding="utf-8"))
        config["verification"]["command"] = [
            sys.executable,
            str(HOOK),
            "--assert-no-token-environment",
        ]
        repo.write_json(repo.config, config)
        resolved = repo.resolve(repo.inventory(), evidence)

        original_gh_token = os.environ.get("GH_TOKEN")
        original_github_token = os.environ.get("GITHUB_TOKEN")
        try:
            os.environ["GH_TOKEN"] = "write-scoped-token"
            os.environ["GITHUB_TOKEN"] = "write-scoped-token"
            result = repo.apply(
                resolved,
                evidence,
                [resolved["candidates"][0]["candidate_id"]],
            )
        finally:
            if original_gh_token is None:
                os.environ.pop("GH_TOKEN", None)
            else:
                os.environ["GH_TOKEN"] = original_gh_token
            if original_github_token is None:
                os.environ.pop("GITHUB_TOKEN", None)
            else:
                os.environ["GITHUB_TOKEN"] = original_github_token

        self.assertTrue(result["has_changes"])

    def test_materialize_reconstructs_only_manifest_authorized_edits(self):
        repo = FixtureRepo(
            self.id().split(".")[-1],
            """
namespace Demo;
public class Tests
{
    [Ignore("#1")][Test] public void ExecutedPass() { }
}
""".lstrip(),
        )
        evidence = repo.evidence(
            {
                "fixture/repo#1": {
                    "kind": "issue",
                    "state": "closed",
                    "state_reason": "completed",
                }
            }
        )
        resolved = repo.resolve(repo.inventory(), evidence)
        source = repo.root / "src" / "Tests.cs"
        original = source.read_bytes()
        result = repo.apply(
            resolved,
            evidence,
            [resolved["candidates"][0]["candidate_id"]],
        )
        authorized = source.read_bytes()
        source.write_bytes(original)

        repo.materialize(resolved, evidence, result)
        self.assertEqual(authorized, source.read_bytes())

        source.write_bytes(original)
        tampered = json.loads(json.dumps(result))
        tampered["retained_candidates"][0]["path"] = "src/Fabricated.cs"
        repo.materialize(resolved, evidence, tampered, expected=20)
        self.assertEqual(original, source.read_bytes())

    def test_final_retained_set_is_reverified_until_stable(self):
        repo = FixtureRepo(
            self.id().split(".")[-1],
            """
namespace Demo;
public class Tests
{
    [Ignore("#1")][Test] public void ExecutedPassOne() { }
    [Ignore("#2")][Test] public void ExecutedPassTwo() { }
}
""".lstrip(),
        )
        evidence = repo.evidence(
            {
                "fixture/repo#1": {
                    "kind": "issue",
                    "state": "closed",
                    "state_reason": "completed",
                },
                "fixture/repo#2": {
                    "kind": "issue",
                    "state": "closed",
                    "state_reason": "completed",
                },
            }
        )
        config = json.loads(repo.config.read_text(encoding="utf-8"))
        config["verification"]["command"] = [
            sys.executable,
            str(HOOK),
            "--fail-when-token-missing",
            "ExecutedPassOne",
            '[Ignore("#2")]',
        ]
        repo.write_json(repo.config, config)
        resolved = repo.resolve(repo.inventory(), evidence)
        result = repo.apply(
            resolved,
            evidence,
            [candidate["candidate_id"] for candidate in resolved["candidates"]],
        )

        retained_names = {
            candidate["test_fqns"][0].rsplit(".", 1)[-1]
            for candidate in result["retained_candidates"]
        }
        self.assertEqual({"ExecutedPassTwo"}, retained_names)
        self.assertIn(
            "final_set:non_passing_outcome:NotExecuted",
            {candidate["reason"] for candidate in result["reverted_candidates"]},
        )
        source = (repo.root / "src" / "Tests.cs").read_text(encoding="utf-8")
        self.assertIn('[Ignore("#1")]', source)
        self.assertNotIn('[Ignore("#2")]', source)

    def test_all_skipped_returns_clean_noop(self):
        repo = FixtureRepo(
            self.id().split(".")[-1],
            """
namespace Demo;
public class Tests
{
    [Ignore("#1")]
    [Test]
    public void OnlySkipped() { }
}
""".lstrip(),
        )
        evidence = repo.evidence(
            {
                "fixture/repo#1": {
                    "kind": "issue",
                    "state": "closed",
                    "state_reason": "completed",
                }
            }
        )
        resolved = repo.resolve(repo.inventory(), evidence)
        original = (repo.root / "src" / "Tests.cs").read_bytes()
        result = repo.apply(
            resolved,
            evidence,
            [resolved["candidates"][0]["candidate_id"]],
            expected=10,
        )
        self.assertFalse(result["has_changes"])
        self.assertEqual(resolved["source_commit"], result["source_commit"])
        self.assertEqual([], result["retained_candidates"])
        self.assertEqual([], result["changed_paths"])
        self.assertEqual(original, (repo.root / "src" / "Tests.cs").read_bytes())
        status = run(["git", "status", "--short"], repo.root).stdout
        self.assertNotIn("src/Tests.cs", status)

    def test_post_verification_eligibility_recheck_is_candidate_scoped(self):
        repo = FixtureRepo(
            self.id().split(".")[-1],
            """
namespace Demo;
public class Tests
{
    [Ignore("#1")][Test] public void ExecutedPassOne() { }
    [Ignore("#2")][Test] public void ExecutedPassTwo() { }
}
""".lstrip(),
        )
        evidence = repo.evidence(
            {
                "fixture/repo#1": {
                    "kind": "issue",
                    "state": "closed",
                    "state_reason": "completed",
                },
                "fixture/repo#2": {
                    "kind": "issue",
                    "state": "closed",
                    "state_reason": "completed",
                },
            }
        )
        config = json.loads(repo.config.read_text(encoding="utf-8"))
        config["verification"]["command"] = [
            sys.executable,
            str(HOOK),
            "--expire",
            str(evidence),
            "fixture/repo#2",
        ]
        repo.write_json(repo.config, config)

        resolved = repo.resolve(repo.inventory(), evidence)
        result = repo.apply(
            resolved,
            evidence,
            [candidate["candidate_id"] for candidate in resolved["candidates"]],
        )
        retained = {
            candidate["candidate_id"] for candidate in result["retained_candidates"]
        }
        expected_retained = {
            candidate["candidate_id"]
            for candidate in resolved["candidates"]
            if candidate["owner"]["method_name"] == "ExecutedPassOne"
        }
        self.assertEqual(expected_retained, retained)
        freshness_reverts = {
            candidate["candidate_id"]
            for candidate in result["reverted_candidates"]
            if candidate["reason"] == "eligibility_changed_after_verification"
        }
        self.assertEqual(
            {
                candidate["candidate_id"]
                for candidate in resolved["candidates"]
                if candidate["owner"]["method_name"] == "ExecutedPassTwo"
            },
            freshness_reverts,
        )
        source = (repo.root / "src" / "Tests.cs").read_text(encoding="utf-8")
        self.assertIn('[Ignore("#2")]', source)
        self.assertNotIn('[Ignore("#1")]', source)

    def test_post_verification_recheck_returns_noop_when_none_remain(self):
        repo = FixtureRepo(
            self.id().split(".")[-1],
            """
namespace Demo;
public class Tests
{
    [Ignore("#1")][Test] public void ExecutedPassOne() { }
    [Ignore("#2")][Test] public void ExecutedPassTwo() { }
}
""".lstrip(),
        )
        evidence = repo.evidence(
            {
                "fixture/repo#1": {
                    "kind": "issue",
                    "state": "closed",
                    "state_reason": "completed",
                },
                "fixture/repo#2": {
                    "kind": "issue",
                    "state": "closed",
                    "state_reason": "completed",
                },
            }
        )
        config = json.loads(repo.config.read_text(encoding="utf-8"))
        config["verification"]["command"] = [
            sys.executable,
            str(HOOK),
            "--expire",
            str(evidence),
            "fixture/repo#1,fixture/repo#2",
        ]
        repo.write_json(repo.config, config)

        resolved = repo.resolve(repo.inventory(), evidence)
        original = (repo.root / "src" / "Tests.cs").read_bytes()
        result = repo.apply(
            resolved,
            evidence,
            [candidate["candidate_id"] for candidate in resolved["candidates"]],
            expected=10,
        )
        self.assertFalse(result["has_changes"])
        self.assertEqual([], result["retained_candidates"])
        self.assertEqual([], result["changed_paths"])
        self.assertEqual(original, (repo.root / "src" / "Tests.cs").read_bytes())
        self.assertEqual(
            {"eligibility_changed_after_verification"},
            {candidate["reason"] for candidate in result["reverted_candidates"]},
        )


if __name__ == "__main__":
    unittest.main(verbosity=2)
