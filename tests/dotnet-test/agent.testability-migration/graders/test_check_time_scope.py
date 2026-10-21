import json
import os
from pathlib import Path
import tempfile
import unittest

from check_time_scope import export_body, validate_program, verify


TARGET = """
public class SubscriptionManager
{
    public Subscription CreateTrial(string userId)
    {
        return new Subscription
        {
            UserId = userId,
            StartedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(14),
            Plan = "trial"
        };
    }

    public bool IsActive(Subscription sub)
    {
        return DateTime.UtcNow < sub.ExpiresAt;
    }

    public void ExportSubscription(Subscription sub)
    {
        File.WriteAllText(sub.UserId, "value");
    }
}
"""
SUBSCRIPTION = """
public class Subscription
{
    public string UserId { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string Plan { get; set; } = "";
}
"""


class TimeScopeTests(unittest.TestCase):
    def test_supported_clock_registration_forms_pass(self):
        original = "var builder = CreateBuilder();\nvar app = builder.Build();\napp.Run();\n"
        for registration in (
            "builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);",
            "builder.Services.AddSingleton(TimeProvider.System);",
            "builder.Services.AddSingleton<System.TimeProvider>(System.TimeProvider.System);",
            "builder.Services.AddSingleton<TimeProvider>(_ => TimeProvider.System);",
            "builder.Services.AddSingleton<TimeProvider>(static services => TimeProvider.System);",
            "builder.Services.TryAddSingleton<TimeProvider>(TimeProvider.System);",
            "builder.Services.AddSingleton(typeof(TimeProvider), TimeProvider.System);",
        ):
            with self.subTest(registration=registration):
                validate_program(
                    original.replace("var app =", registration + "\nvar app ="),
                    original,
                )

    def test_token_only_clock_registration_mutations_fail(self):
        original = "var builder = CreateBuilder();\nvar app = builder.Build();\napp.Run();\n"
        for registration in (
            "builder.Services.AddSingleton(nameof(TimeProvider.System));",
            'builder.Services.AddSingleton("TimeProvider.System");',
            "builder.Services.AddSingleton(typeof(string), TimeProvider.System);",
            "builder.Services.AddSingleton<TimeProvider>(_ => null);",
            "builder.Services.Remove(TimeProvider.System);",
        ):
            with self.subTest(registration=registration):
                with self.assertRaisesRegex(ValueError, "unrelated Program statement"):
                    validate_program(
                        original.replace("var app =", registration + "\nvar app ="),
                        original,
                    )

    def test_clock_registration_after_host_build_fails(self):
        original = "var builder = CreateBuilder();\nvar app = builder.Build();\napp.Run();\n"
        with self.assertRaisesRegex(ValueError, "before builder.Build"):
            validate_program(
                original.replace(
                    "app.Run();",
                    "builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);\napp.Run();",
                ),
                original,
            )

    def test_production_clock_registration_is_required_not_just_a_comment(self):
        original = "var builder = CreateBuilder();\nRun(builder);\n"
        for current in (original, original + "// TimeProvider.System registration goes here\n"):
            with self.subTest(current=current):
                with self.assertRaisesRegex(ValueError, "production TimeProvider registration"):
                    validate_program(current, original)
        validate_program(
            "var builder = CreateBuilder();\n"
            "builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);\n"
            "Run(builder);\n",
            original,
        )
        validate_program(
            "var builder = CreateBuilder();\n"
            "builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);\n"
            "builder.Services.AddTransient<FullPipeline.Services.SubscriptionManager>();\n"
            "Run(builder);\n",
            original,
        )

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
            program = workspace / "FullPipeline/Program.cs"
            project = workspace / "FullPipeline/FullPipeline.csproj"
            source.parent.mkdir(parents=True)
            source.write_text(TARGET + SUBSCRIPTION, encoding="utf-8")
            program.write_text("var builder = CreateBuilder();\nRun(builder);\n", encoding="utf-8")
            project.write_text("<Project />\n", encoding="utf-8")
            baseline = {
                source.relative_to(workspace).as_posix(): source.read_bytes().hex(),
                program.relative_to(workspace).as_posix(): program.read_bytes().hex(),
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
                program.write_text(
                    "var builder = CreateBuilder();\n"
                    "builder.Services.AddSingleton(TimeProvider.System);\n"
                    "Run(builder);\n",
                    encoding="utf-8",
                )
                verify()
                project.write_text("<Project Sdk=\"changed\" />\n", encoding="utf-8")
                with self.assertRaisesRegex(ValueError, "production project"):
                    verify()
            finally:
                os.chdir(previous)

    def test_out_of_scope_source_and_program_changes_fail(self):
        with tempfile.TemporaryDirectory() as directory:
            workspace = Path(directory)
            source = workspace / "FullPipeline/Services/SubscriptionManager.cs"
            program = workspace / "FullPipeline/Program.cs"
            project = workspace / "FullPipeline/FullPipeline.csproj"
            source.parent.mkdir(parents=True)
            source.write_text(TARGET + SUBSCRIPTION, encoding="utf-8")
            program.write_text("var builder = CreateBuilder();\nRun(builder);\n", encoding="utf-8")
            project.write_text("<Project />\n", encoding="utf-8")
            baseline = {
                path.relative_to(workspace).as_posix(): path.read_bytes().hex()
                for path in (source, program, project)
            }
            (workspace / ".eval").mkdir()
            (workspace / ".eval/baseline.json").write_text(
                json.dumps(baseline, sort_keys=True), encoding="utf-8"
            )
            previous = Path.cwd()
            try:
                os.chdir(workspace)
                source.write_text(
                    (TARGET + SUBSCRIPTION).replace(
                        'File.WriteAllText(sub.UserId, "value");',
                        "Console.WriteLine(sub.UserId);",
                    ),
                    encoding="utf-8",
                )
                with self.assertRaisesRegex(ValueError, "ExportSubscription"):
                    verify()
                source.write_text(TARGET + SUBSCRIPTION, encoding="utf-8")
                program.write_text("Run(CreateBuilder());\n", encoding="utf-8")
                with self.assertRaisesRegex(ValueError, "Program"):
                    verify()
                program.write_text(
                    "var builder = CreateBuilder();\n"
                    "if (false) { Run(builder); }\n",
                    encoding="utf-8",
                )
                with self.assertRaisesRegex(ValueError, "Program"):
                    verify()
                program.write_text(
                    "var builder = CreateBuilder();\nRun(builder);\n", encoding="utf-8"
                )
                source.write_text(
                    (TARGET + SUBSCRIPTION).replace(
                        "\n}\n",
                        "\n    public void UnrelatedMethod() { }\n}\n",
                        1,
                    ),
                    encoding="utf-8",
                )
                with self.assertRaisesRegex(ValueError, "Unexpected SubscriptionManager member"):
                    verify()
            finally:
                os.chdir(previous)

    def test_only_time_access_can_change_in_time_methods(self):
        with tempfile.TemporaryDirectory() as directory:
            workspace = Path(directory)
            source = workspace / "FullPipeline/Services/SubscriptionManager.cs"
            program = workspace / "FullPipeline/Program.cs"
            project = workspace / "FullPipeline/FullPipeline.csproj"
            source.parent.mkdir(parents=True)
            source.write_text(TARGET + SUBSCRIPTION, encoding="utf-8")
            program.write_text("var builder = CreateBuilder();\nRun(builder);\n", encoding="utf-8")
            project.write_text("<Project />\n", encoding="utf-8")
            baseline = {
                path.relative_to(workspace).as_posix(): path.read_bytes().hex()
                for path in (source, program, project)
            }
            (workspace / ".eval").mkdir()
            (workspace / ".eval/baseline.json").write_text(
                json.dumps(baseline, sort_keys=True), encoding="utf-8"
            )
            migrated = (TARGET + SUBSCRIPTION).replace(
                "DateTime.UtcNow", "_timeProvider.GetUtcNow().UtcDateTime"
            )
            migrated = migrated.replace(
                "public class SubscriptionManager\n{",
                "public class SubscriptionManager\n{\n"
                "    private readonly TimeProvider _timeProvider;\n"
                "    public SubscriptionManager(TimeProvider timeProvider) "
                "{ _timeProvider = timeProvider; }",
            )
            previous = Path.cwd()
            try:
                os.chdir(workspace)
                source.write_text(migrated, encoding="utf-8")
                program.write_text(
                    "var builder = CreateBuilder();\n"
                    "builder.Services.AddSingleton(TimeProvider.System);\n"
                    "Run(builder);\n",
                    encoding="utf-8",
                )
                verify()
                source.write_text(migrated.replace('Plan = "trial"', 'Plan = "changed"'), encoding="utf-8")
                with self.assertRaisesRegex(ValueError, "CreateTrial"):
                    verify()
                source.write_text(
                    migrated.replace(
                        "_timeProvider.GetUtcNow().UtcDateTime < sub.ExpiresAt",
                        "_timeProvider.GetUtcNow().UtcDateTime <= sub.ExpiresAt",
                    ),
                    encoding="utf-8",
                )
                with self.assertRaisesRegex(ValueError, "IsActive"):
                    verify()
            finally:
                os.chdir(previous)


if __name__ == "__main__":
    unittest.main()
