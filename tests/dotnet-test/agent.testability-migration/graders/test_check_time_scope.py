import json
import os
from pathlib import Path
import tempfile
import unittest

from check_time_scope import export_body, verify


TARGET = """
public class SubscriptionManager
{
    public void ExportSubscription(Subscription sub)
    {
        File.WriteAllText(sub.UserId, "value");
    }
}
"""


class TimeScopeTests(unittest.TestCase):
    def test_unique_target_body_is_extracted(self):
        self.assertIn("WriteAllText", export_body(TARGET))

    def test_different_overload_does_not_mask_target(self):
        source = TARGET.replace(
            "public void ExportSubscription(Subscription sub)",
            "public void ExportSubscription(string sub) { }\n"
            "    public void ExportSubscription(Subscription sub)",
        )
        self.assertEqual(export_body(source), export_body(TARGET))

    def test_duplicate_exact_signature_is_rejected(self):
        duplicate = TARGET.replace(
            "\n}",
            "\n    public void ExportSubscription(Subscription sub) { }\n}",
        )
        with self.assertRaisesRegex(ValueError, "exactly one"):
            export_body(duplicate)

    def test_changed_target_is_detected_despite_overload(self):
        source = TARGET.replace(
            "public void ExportSubscription(Subscription sub)",
            "public void ExportSubscription(string sub) { }\n"
            "    public void ExportSubscription(Subscription sub)",
        ).replace('File.WriteAllText(sub.UserId, "value");', "Console.WriteLine(sub.UserId);")
        self.assertNotEqual(export_body(source), export_body(TARGET))

    def test_sibling_tests_are_allowed_but_production_project_edits_fail(self):
        with tempfile.TemporaryDirectory() as directory:
            workspace = Path(directory)
            source = workspace / "FullPipeline/Services/SubscriptionManager.cs"
            project = workspace / "FullPipeline/FullPipeline.csproj"
            source.parent.mkdir(parents=True)
            source.write_text(TARGET, encoding="utf-8")
            project.write_text("<Project />\n", encoding="utf-8")
            baseline = {
                source.relative_to(workspace).as_posix(): source.read_bytes().hex(),
                project.relative_to(workspace).as_posix(): project.read_bytes().hex(),
            }
            eval_dir = workspace / ".eval"
            eval_dir.mkdir()
            (eval_dir / "baseline.json").write_text(
                json.dumps(baseline, sort_keys=True), encoding="utf-8"
            )
            sibling = workspace / "FullPipeline.Tests"
            sibling.mkdir()
            (sibling / "FullPipeline.Tests.csproj").write_text(
                "<Project />\n", encoding="utf-8"
            )
            previous = Path.cwd()
            try:
                os.chdir(workspace)
                verify()
                project.write_text("<Project Sdk=\"changed\" />\n", encoding="utf-8")
                with self.assertRaisesRegex(ValueError, "production project"):
                    verify()
            finally:
                os.chdir(previous)


if __name__ == "__main__":
    unittest.main()
