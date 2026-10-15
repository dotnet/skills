"""Replay default-collection migration and reject broken scheduling/lifetime."""

import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

import yaml


SPEC = Path(__file__).with_name("eval.yaml")
CASE = next(
    item for item in yaml.safe_load(SPEC.read_text(encoding="utf-8"))["stimuli"]
    if item["name"] == "Preserve an ordinary shared collection without a disable-parallelization flag"
)
replay_spec = importlib.util.spec_from_file_location(
    "replay", SPEC.parent.parent / "graders" / "replay.py"
)
replay = importlib.util.module_from_spec(replay_spec)
replay_spec.loader.exec_module(replay)
report_spec = importlib.util.spec_from_file_location(
    "collection_report", SPEC.parent.parent / "graders" / "collection_report.py"
)
report = importlib.util.module_from_spec(report_spec)
report_spec.loader.exec_module(report)
RESOURCE_REPORT = (
    "Both members share one resource and serialize on the same exclusive key. "
    "MSTest permits unrelated classes to run in parallel, so this avoids the "
    "broader assembly serialization; lifecycle remains assembly-wide for this fixture."
)
DEFERRED_REPORT = (
    "Unrelated classes may run in parallel with each other, but not with "
    "FirstMember or SecondMember. This is coarser scheduling than xUnit collections."
)


class DefaultCollectionTests(unittest.TestCase):
    def test_omitted_predicate_preserves_negation_and_targets(self):
        for text in (
            "Unrelated classes can run in parallel with each other, but not with FirstMember or SecondMember.",
            "Unrelated classes can overlap each other, but not these deferred members.",
            "Unrelated classes can run in parallel with each other, but they cannot with FirstMember or SecondMember.",
        ):
            with self.subTest(text=text):
                report.validate_report("deferred-members", text)
        for text in (
            "Unrelated classes can run in parallel with each other, but also with FirstMember or SecondMember.",
            "Unrelated classes cannot run in parallel with each other, but can with FirstMember or SecondMember.",
        ):
            with self.subTest(text=text), self.assertRaisesRegex(AssertionError, "Scheduling disclosure"):
                report.validate_report("deferred-members", text)
        with self.assertRaisesRegex(AssertionError, "Scheduling disclosure"):
            report.validate_report(
                "assembly-serial",
                "Unrelated classes cannot run in parallel with each other, but can with these members.",
            )

    def test_pronouns_and_named_members_keep_scheduling_subject(self):
        for text in (
            "Unrelated classes may run in parallel with each other, but they cannot overlap these deferred members.",
            "Unrelated classes may run in parallel with each other, but cannot overlap FirstMember or SecondMember.",
            "Unrelated classes may run in parallel with each other. They cannot overlap FirstMember or SecondMember.",
        ):
            with self.subTest(report=text):
                report.validate_report("deferred-members", text)
        for text in (
            "Unrelated classes cannot overlap each other, but they may overlap these members.",
            "Unrelated classes cannot overlap each other. They may overlap FirstMember or SecondMember.",
            "Unrelated classes cannot overlap each other, and they may overlap these members.",
        ):
            with self.subTest(report=text), self.assertRaisesRegex(AssertionError, "Scheduling disclosure"):
                report.validate_report("assembly-serial", text)

    def workspace(self, parent):
        root = Path(tempfile.mkdtemp(prefix="default-collection-", dir=parent))
        replay.materialize(SPEC, CASE, root)
        return root

    def test_source_collection_has_no_disable_flag(self):
        source = SPEC.parent / "fixtures" / "default-collection" / "CollectionTests.cs"
        text = source.read_text(encoding="utf-8")
        self.assertIn('[CollectionDefinition("Shared")]', text)
        self.assertNotIn("DisableParallelization", text)

    def test_golden_and_broken_scheduling_and_disposal(self):
        with tempfile.TemporaryDirectory(prefix="collection-contract-") as parent:
            root = self.workspace(parent)
            replay.run_graders(CASE, root)
            with self.assertRaisesRegex(AssertionError, "Scheduling disclosure"):
                replay.run_graders(
                    CASE, root,
                    "MSTest serializes unrelated classes. They may still run in "
                    "parallel with one another. This is conservative scheduling.",
                )
            with self.assertRaisesRegex(AssertionError, "Scheduling disclosure"):
                replay.run_graders(
                    CASE, root, "Unrelated classes cannot overlap these members. "
                    "This is conservative scheduling.",
                )
            with self.assertRaisesRegex(AssertionError, "Scheduling disclosure"):
                replay.run_graders(
                    CASE, root,
                    "The two members serialize; unrelated classes remain parallel "
                    "and can overlap exactly as in xUnit.",
                )
            source = root / "CollectionTests.cs"
            golden = source.read_text(encoding="utf-8")

            # Activating framework parallelism must not invalidate serialized members.
            parallel = golden.replace(
                "using Microsoft.VisualStudio.TestTools.UnitTesting;",
                "using Microsoft.VisualStudio.TestTools.UnitTesting;\n"
                "[assembly: Parallelize(Workers = 2, Scope = ExecutionScope.ClassLevel)]",
            )
            source.write_text(
                parallel.replace("Workers = 2", "Workers = 1").replace("[DoNotParallelize]\n", ""),
                encoding="utf-8",
            )
            replay.run_graders(
                CASE, root, "MSTest serializes unrelated classes under this assembly policy."
            )
            source.write_text(parallel, encoding="utf-8")
            with self.assertRaisesRegex(AssertionError, "Scheduling disclosure"):
                replay.run_graders(CASE, root)
            replay.run_graders(CASE, root, DEFERRED_REPORT)
            for invalid in (
                "Unrelated classes cannot overlap each other, but may overlap "
                "these deferred members. This is coarser scheduling.",
                "Unrelated classes may run in parallel with each other, and these "
                "deferred members may overlap unrelated classes. This is coarser scheduling.",
                "MSTest serializes unrelated classes under this assembly policy.",
            ):
                with self.subTest(deferred_report=invalid):
                    with self.assertRaisesRegex(AssertionError, "Scheduling disclosure"):
                        replay.run_graders(CASE, root, invalid)

            for member in ("FirstMember", "SecondMember"):
                with self.subTest(partially_deferred=member):
                    source.write_text(parallel.replace(
                        f"[DoNotParallelize]\npublic class {member}",
                        f"public class {member}",
                    ), encoding="utf-8")
                    with self.assertRaisesRegex(AssertionError, "both members must be deferred"):
                        replay.run_graders(CASE, root, DEFERRED_REPORT)

            resource_locks = parallel.replace(
                "[DoNotParallelize]", '[ResourceLock("Shared")]'
            )
            source.write_text(resource_locks, encoding="utf-8")
            replay.run_graders(CASE, root, RESOURCE_REPORT)
            with self.assertRaisesRegex(AssertionError, "Scheduling disclosure"):
                replay.run_graders(CASE, root)

            source.write_text(resource_locks.replace(
                '[ResourceLock("Shared")]', '[ResourceLock("Other")]', 1
            ), encoding="utf-8")
            with self.assertRaisesRegex(AssertionError, "Collection scheduling contract"):
                replay.run_graders(CASE, root)

            source.write_text(parallel.replace("[DoNotParallelize]\n", ""), encoding="utf-8")
            with self.assertRaisesRegex(AssertionError, "Collection scheduling contract"):
                replay.run_graders(CASE, root)

            source.write_text(
                golden.replace(
                    "public static void Cleanup() => Resource.Dispose();",
                    "public static void Cleanup() { }",
                ), encoding="utf-8",
            )
            with self.assertRaises(AssertionError):
                replay.run_graders(CASE, root)

    def test_original_xunit_fixture_is_healthy_and_shares_one_resource(self):
        with tempfile.TemporaryDirectory(prefix="collection-baseline-") as parent:
            root = Path(parent)
            baseline = dict(CASE)
            baseline.pop("golden_patch")
            replay.materialize(SPEC, baseline, root)
            result = subprocess.run(
                [
                    sys.executable, ".eval/migration_evidence.py", "run",
                    "--project", "TestProject.csproj", "--runner", "vstest",
                    "--expect", "FirstMember.FirstCase=Passed",
                    "--expect", "SecondMember.SecondCase=Passed",
                ],
                cwd=root, capture_output=True, text=True, timeout=180,
            )
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            events = [
                json.loads(line)
                for line in next(root.glob(".eval/results/*/lifecycle.jsonl")).read_text(
                    encoding="utf-8"
                ).splitlines()
            ]
            self.assertEqual(1, len({event["instance"] for event in events}))
            self.assertEqual(
                ["created", "entered", "exited", "entered", "exited", "disposed"],
                [event["event"] for event in events],
            )
            self.assertEqual(
                {"FirstCase", "SecondCase"},
                {event["case"] for event in events if event["event"] == "entered"},
            )

    def test_tradeoff_cannot_be_omitted_from_the_report(self):
        with tempfile.TemporaryDirectory(prefix="collection-report-") as parent:
            root = Path(parent)
            output_only = dict(CASE, graders=[
                grader for grader in CASE["graders"]
                if grader["type"].startswith("output-")
            ])
            replay.run_graders(output_only, root)
            replay.run_graders(
                output_only, root,
                "The shared resource is initialized once and disposed after all tests. "
                "Assembly lifecycle slightly widens its lifetime. MSTest's default "
                "scheduling also serializes unrelated classes, whereas xUnit permits "
                "unrelated collections to overlap.",
            )
            with self.assertRaises(AssertionError):
                replay.run_graders(
                    output_only, root, "Migrated and both tests pass with exact parity."
                )

    def test_report_polarity_matches_the_observed_policy(self):
        serial_report = (
            "MSTest serializes unrelated classes under this assembly policy, "
            "whereas xUnit allows unrelated collections to run concurrently."
        )
        deferred_report = (
            "Unrelated classes cannot overlap with these members. "
            "This is a coarser policy than xUnit collections."
        )
        reversed_report = "Unrelated classes remain parallel and can overlap exactly as in xUnit."
        for policy, positives in (
            ("assembly-serial", (serial_report,)),
            ("deferred-members", (deferred_report, DEFERRED_REPORT)),
        ):
            for valid in positives:
                with self.subTest(policy=policy, valid=valid):
                    report.validate_report(policy, valid)
            for invalid in (
                reversed_report, serial_report + " " + reversed_report,
                "Unrelated classes are not serialized in MSTest.",
                "MSTest does not serialize unrelated classes.",
            ):
                with self.subTest(policy=policy, invalid=invalid):
                    with self.assertRaisesRegex(AssertionError, "Scheduling disclosure"):
                        report.validate_report(policy, invalid)
        report.validate_report("shared-resource", RESOURCE_REPORT)
        report.validate_report(
            "shared-resource", "MSTest does not serialize unrelated classes."
        )
        report.validate_report(
            "shared-resource", "Unrelated classes are not blocked in MSTest."
        )
        with self.assertRaisesRegex(AssertionError, "Scheduling disclosure"):
            report.validate_report("shared-resource", deferred_report)

    def test_overlap_targets_and_negation_are_not_interchangeable(self):
        positives = {
            "assembly-serial": (
                "Unrelated classes cannot overlap each other or these members.",
                "MSTest does not allow unrelated classes to run concurrently.",
            ),
            "deferred-members": (
                DEFERRED_REPORT,
                DEFERRED_REPORT.replace(" parallel ", " parallel\n").replace(
                    "these deferred", "these\ndeferred"
                ),
                "Unrelated classes may run concurrently with one another, but unrelated "
                "classes cannot run concurrently with these members.",
                "These deferred members cannot overlap unrelated classes, but unrelated "
                "classes can overlap each other.",
                "These deferred members may not overlap unrelated classes.",
                "Unrelated classes can overlap each other but are not allowed to overlap "
                "these deferred members.",
                "Unrelated classes cannot overlap these members, but aren't blocked "
                "from running in parallel with each other.",
                "Unrelated classes cannot overlap these members, whereas xUnit allows "
                "unrelated classes to run in parallel and unrelated collections may overlap.",
                "In xUnit unrelated collections may overlap, whereas unrelated classes "
                "cannot overlap these deferred members.",
                "Unrelated classes cannot overlap these members, but xUnit allows unrelated "
                "classes to run in parallel and unrelated collections can overlap.",
            ),
            "shared-resource": (
                "Unrelated classes may run in parallel with each other and these members.",
                "These serialized members cannot overlap each other, but may overlap "
                "unrelated classes.",
                "Unrelated classes can overlap these serialized members, but these "
                "members cannot overlap each other.",
                "Unrelated classes are not serialized with these members.",
            ),
        }
        negatives = {
            "assembly-serial": (
                DEFERRED_REPORT,
                "Unrelated classes cannot overlap these members, but may overlap each other.",
            ),
            "deferred-members": (
                "Unrelated classes cannot overlap each other, but may overlap these members.",
                "Unrelated classes may not run in parallel with each other, but can overlap "
                "these deferred members.",
                "Unrelated classes can overlap each other but cannot overlap one another.",
                DEFERRED_REPORT + " These deferred members may overlap unrelated classes.",
                DEFERRED_REPORT + " Unrelated classes may overlap each other and these members.",
                "Unrelated classes cannot overlap these members, yet can overlap these members.",
                "Unrelated classes cannot overlap these members, however may overlap these members.",
                "Unrelated classes cannot overlap these members, but aren't blocked "
                "from overlapping these members.",
                "MSTest serializes unrelated classes under this assembly policy.",
            ),
            "shared-resource": (
                DEFERRED_REPORT,
                RESOURCE_REPORT + " These members cannot overlap unrelated classes.",
                "MSTest does not allow unrelated classes to run in parallel.",
                "MSTest doesn't permit unrelated classes to run concurrently.",
                "Unrelated classes may not overlap these members.",
                "Unrelated classes are not allowed to overlap these members.",
                "Unrelated classes can not run in parallel.",
            ),
        }
        for policy, responses in positives.items():
            for response in responses:
                with self.subTest(policy=policy, positive=response):
                    report.validate_report(policy, response)
        for policy, responses in negatives.items():
            for response in responses:
                with self.subTest(policy=policy, negative=response):
                    with self.assertRaisesRegex(AssertionError, "Scheduling disclosure"):
                        report.validate_report(policy, response)

    def test_leading_comparison_retains_current_framework_claims(self):
        contrasts = (
            "Unlike xUnit, unrelated classes cannot overlap these deferred members, "
            "but can run in parallel with each other.",
            "Unlike xUnit, MSTest ensures unrelated classes cannot overlap "
            "these deferred members.",
        )
        for response in contrasts:
            with self.subTest(response=response):
                report.validate_report("deferred-members", response)
                with self.assertRaisesRegex(AssertionError, "Scheduling disclosure"):
                    report.validate_report(
                        "deferred-members", response.replace("cannot overlap", "can overlap")
                    )
        report.validate_report(
            "assembly-serial", "Unlike xUnit, MSTest serializes unrelated classes."
        )
        report.validate_report(
            "shared-resource", "Unlike xUnit, MSTest allows unrelated classes to overlap these members."
        )
        report.validate_report(
            "deferred-members",
            "In xUnit unrelated classes can overlap, MSTest does not allow unrelated "
            "classes to overlap these deferred members.",
        )

    def test_shared_resource_report_must_address_collection_members(self):
        peers = "Unrelated classes can overlap each other."
        with self.assertRaisesRegex(AssertionError, "Scheduling disclosure"):
            report.validate_report("shared-resource", peers)
        report.validate_report(
            "shared-resource", peers + " Unrelated classes may overlap these members."
        )

    def test_assembly_serial_disclosure_must_cover_unrelated_peers(self):
        for response in (
            "Unrelated classes cannot overlap these members.",
            "Unrelated classes may run in parallel with each other, but cannot overlap these members.",
        ):
            with self.subTest(response=response):
                with self.assertRaisesRegex(AssertionError, "Scheduling disclosure"):
                    report.validate_report("assembly-serial", response)
        for response in (
            "MSTest serializes unrelated classes under this assembly policy.",
            "Unrelated classes cannot overlap each other or these members.",
        ):
            with self.subTest(response=response):
                report.validate_report("assembly-serial", response)

    def test_pronoun_continuations_retain_subject_and_framework(self):
        for policy, valid, invalid in (
            (
                "assembly-serial",
                "MSTest serializes unrelated classes. They cannot overlap one another.",
                "MSTest serializes unrelated classes. They may still run in parallel with one another.",
            ),
            (
                "deferred-members",
                DEFERRED_REPORT + " They cannot overlap these members.",
                DEFERRED_REPORT + " They may overlap these members.",
            ),
            (
                "shared-resource",
                RESOURCE_REPORT + " They can overlap these members.",
                RESOURCE_REPORT + " They cannot overlap these members.",
            ),
        ):
            with self.subTest(policy=policy):
                report.validate_report(policy, valid)
                with self.assertRaisesRegex(AssertionError, "Scheduling disclosure"):
                    report.validate_report(policy, invalid)
        report.validate_report(
            "assembly-serial",
            "MSTest serializes unrelated classes. In xUnit unrelated collections "
            "may overlap. They may run in parallel with one another.",
        )
        with self.assertRaisesRegex(AssertionError, "unresolved pronoun"):
            report.validate_report("assembly-serial", "They may run in parallel with one another.")


if __name__ == "__main__":
    unittest.main()
