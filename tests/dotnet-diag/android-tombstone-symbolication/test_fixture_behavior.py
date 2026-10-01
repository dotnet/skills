from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import unittest
import yaml

ANDROID = Path(__file__).resolve().parent
APPLE = ANDROID.parent / "apple-crash-symbolication"
ROOT = ANDROID.parents[2]
ANDROID_SCRIPT = ROOT / "plugins/dotnet-diag/skills/android-tombstone-symbolication/scripts/Symbolicate-Tombstone.ps1"
APPLE_SCRIPT = ROOT / "plugins/dotnet-diag/skills/apple-crash-symbolication/scripts/Symbolicate-Crash.ps1"


def invoke(script, parameter, fixture, *flags):
    return subprocess.run(
        ["pwsh", "-NoProfile", "-File", str(script), parameter, str(fixture), *flags],
        capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=60,
    )


class GoldenReferenceTests(unittest.TestCase):
    def test_golden_text_and_fixture_graders_accept(self):
        for suite in (ANDROID, APPLE):
            document = yaml.safe_load((suite / "eval.yaml").read_text(encoding="utf-8"))
            for stimulus in document["stimuli"]:
                with self.subTest(suite=suite.name, stimulus=stimulus["name"]):
                    output = stimulus["golden_trajectory"]["inline"]["steps"][-1]["message"]
                    with tempfile.TemporaryDirectory(prefix=".grader-", dir=suite) as temp:
                        workspace = Path(temp)
                        for item in stimulus.get("environment", {}).get("files", []):
                            source, destination = (suite / item["src"]).resolve(), workspace / item["dest"]
                            destination.parent.mkdir(parents=True, exist_ok=True)
                            shutil.copy2(source, destination)
                        for grader in stimulus["graders"]:
                            kind, config = grader["type"], grader.get("config", {})
                            if kind == "output-matches":
                                self.assertIsNotNone(re.search(config["pattern"], output))
                            elif kind == "output-not-matches":
                                self.assertIsNone(re.search(config["pattern"], output))
                            elif kind == "file-exists":
                                self.assertTrue((workspace / config["path"]).exists())
                            elif kind == "run-command":
                                result = subprocess.run(
                                    config["command"], cwd=workspace, shell=True,
                                    capture_output=True, text=True, timeout=60,
                                )
                                self.assertEqual(result.returncode, config.get("expected_exit_code", 0),
                                                 result.stdout + result.stderr)
                                self.assertRegex(result.stdout, config.get("stdout_matches", ".*"))


class ParserBehaviorTests(unittest.TestCase):
    def test_golden_crash_fixtures_are_accepted_hermetically(self):
        android_cases = {
            "tombstone_sample.txt": "libmonosgen-2.0.so",
            "tombstone_coreclr.txt": "libcoreclr.so",
            "tombstone_nativeaot.txt": "libSystem.Native.so",
            "tombstone_multithread.txt": "Finalizer",
            "tombstone_no_buildid.txt": "Frames Without BuildId",
            "tombstone_multi_buildid.txt": "libSystem.Globalization.Native.so",
            "fixtures/logcat-prefixed.txt": "libSystem.Native.so",
        }
        for relative, marker in android_cases.items():
            with self.subTest(fixture=relative):
                result = invoke(ANDROID_SCRIPT, "-TombstoneFile", ANDROID / relative,
                                "-ParseOnly", "-SkipVersionLookup")
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                self.assertIn(marker, result.stdout + result.stderr)

        apple_cases = {
            "ios_crash.ips": "87d24fda191f393880634c56f505eb9d",
            "fixtures/mono-ios.ips": "libmonosgen-2.0",
            "fixtures/macos-version.ips": "0x200001234",
            "fixtures/nativeaot-ios.ips": "0x104002000",
            "fixtures/duplicate-case-keys.ips": "0x140000abc",
            "fixtures/no-dotnet.ips": "No .NET runtime libraries",
        }
        for relative, marker in apple_cases.items():
            with self.subTest(fixture=relative):
                result = invoke(APPLE_SCRIPT, "-CrashFile", APPLE / relative,
                                "-ParseOnly", "-SkipVersionLookup", "-SkipSymbolDownload")
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                self.assertIn(marker, result.stdout + result.stderr)

    def test_malformed_wrong_format_and_mutated_inputs_are_rejected(self):
        rejected = (
            (ANDROID_SCRIPT, "-TombstoneFile", ANDROID / "crashlog_ios.txt",
             ("-ParseOnly", "-SkipVersionLookup")),
            (ANDROID_SCRIPT, "-TombstoneFile", ANDROID / "fixtures/malformed-tombstone.txt",
             ("-ParseOnly", "-SkipVersionLookup")),
            (APPLE_SCRIPT, "-CrashFile", APPLE / "tombstone_sample.txt",
             ("-ParseOnly", "-SkipVersionLookup", "-SkipSymbolDownload")),
            (APPLE_SCRIPT, "-CrashFile", APPLE / "fixtures/legacy.crash",
             ("-ParseOnly", "-SkipVersionLookup", "-SkipSymbolDownload")),
            (APPLE_SCRIPT, "-CrashFile", APPLE / "fixtures/malformed.ips",
             ("-ParseOnly", "-SkipVersionLookup", "-SkipSymbolDownload")),
        )
        for script, parameter, fixture, flags in rejected:
            with self.subTest(fixture=fixture.name):
                result = invoke(script, parameter, fixture, *flags)
                self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)

        with tempfile.TemporaryDirectory(prefix=".mutation-", dir=ANDROID) as temp:
            path = Path(temp) / "mutated.txt"
            path.write_text((ANDROID / "tombstone_sample.txt").read_text().replace("#", "frame-"))
            self.assertNotEqual(invoke(ANDROID_SCRIPT, "-TombstoneFile", path,
                                       "-ParseOnly", "-SkipVersionLookup").returncode, 0)

        with tempfile.TemporaryDirectory(prefix=".mutation-", dir=APPLE) as temp:
            path = Path(temp) / "mutated.ips"
            metadata, body = (APPLE / "ios_crash.ips").read_text().split("\n", 1)
            path.write_text(metadata + "\n" + body[:-20])
            self.assertNotEqual(invoke(APPLE_SCRIPT, "-CrashFile", path, "-ParseOnly",
                                       "-SkipVersionLookup", "-SkipSymbolDownload").returncode, 0)


if __name__ == "__main__":
    unittest.main()
