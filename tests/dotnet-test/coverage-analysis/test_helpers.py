"""Process-level regressions for the shipped Cobertura helpers (Python + pwsh)."""

import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[3]
SCRIPTS = ROOT / "plugins/dotnet-test/skills/coverage-analysis/scripts"
EXISTING = Path(__file__).parent / "fixtures/partial-coverage/coverage.cobertura.xml"


class CoverageHelperTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.workspace = Path(self.temp.name)
        self.pwsh = shutil.which("pwsh")
        self.assertIsNotNone(self.pwsh, "PowerShell 7 is required; do not skip helper verification.")

    def report(self, name, hits=(1, 0), branch=None, complexity="2", class_lines=True, signature="()"):
        root = ET.Element("coverage", {
            "timestamp": name, "lines-covered": str(sum(hit > 0 for hit in hits)),
            "lines-valid": str(len(hits)), "branches-covered": str(branch[0] if branch else 0),
            "branches-valid": str(branch[1] if branch else 0),
        })
        package = ET.SubElement(ET.SubElement(root, "packages"), "package", name="Sample")
        cls = ET.SubElement(ET.SubElement(package, "classes"), "class", name="Sample", filename="src/sample.cs")
        method = ET.SubElement(ET.SubElement(cls, "methods"), "method", name="Work", signature=signature)
        if complexity is not None:
            method.set("complexity", complexity)
        lines = ET.SubElement(method, "lines")
        for number, hit in enumerate(hits, 1):
            attrs = {"number": str(number), "hits": str(hit), "branch": "false"}
            if number == 1 and branch:
                attrs.update(branch="true", **{"condition-coverage": f"{100 * branch[0] / branch[1]}% ({branch[0]}/{branch[1]})"})
            ET.SubElement(lines, "line", attrs)
        if class_lines:
            cls.append(ET.fromstring(ET.tostring(lines)))
        path = self.workspace / name
        ET.ElementTree(root).write(path, encoding="utf-8", xml_declaration=True)
        return path

    def run_helper(self, paths, script="Extract-MethodCoverage.ps1", legacy=False, extra="", culture=""):
        env = {
            **os.environ, "HELPER_PATH": str(SCRIPTS / script),
            "HELPER_INPUTS": json.dumps([str(path) for path in paths]),
            "HELPER_CULTURE": culture,
        }
        command = (
            "if ($env:HELPER_CULTURE) { [cultureinfo]::CurrentCulture = $env:HELPER_CULTURE }; "
            "& $env:HELPER_PATH -CoberturaPath (ConvertFrom-Json $env:HELPER_INPUTS) "
            f"-OutputFormat {'legacy' if legacy else 'json-v1'} {extra}; exit $LASTEXITCODE"
        )
        result = subprocess.run(
            [self.pwsh, "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", command],
            capture_output=True, text=True, env=env, timeout=45,
        )
        if legacy:
            return result, None
        self.assertTrue(result.stdout.strip(), result.stderr)
        data = json.loads(result.stdout)
        self.assertEqual(data["schema_version"], "1")
        self.assertEqual(data["tool_version"], "1.0")
        self.assertEqual(result.returncode, {"complete": 0, "partial": 3, "error": 2}[data["status"]], result.stderr)
        return result, data

    def test_duplicate_report_and_duplicate_bytes_are_idempotent(self):
        _, single = self.run_helper([EXISTING])
        duplicate = self.workspace / "different-name.xml"
        duplicate.write_bytes(EXISTING.read_bytes())
        for paths in ([EXISTING, EXISTING], [EXISTING, duplicate]):
            with self.subTest(paths=paths):
                _, repeated = self.run_helper(paths)
                self.assertEqual(single["data"], repeated["data"])
        values = {item["Method"]: item["BranchCoverage"] for item in single["data"]["methods"]}
        self.assertEqual((values["Search"], values["Enroll"], values["CalculateGpa"]), (50, 66.7, 25))

    def test_distinct_anonymous_reports_are_bounded_not_added(self):
        a = self.report("a.xml", branch=(1, 2))
        b = self.report("b.xml", branch=(1, 2))
        _, output = self.run_helper([a, b])
        method = output["data"]["methods"][0]
        self.assertEqual(output["status"], "partial")
        self.assertIsNone(method["BranchCoverage"])
        self.assertIsNone(method["CoveredBranches"])
        self.assertEqual((method["BranchCoverageLower"], method["BranchCoverageUpper"]), (50, 100))
        for paths in ([b, a], [a, b, a]):
            _, reordered = self.run_helper(paths)
            self.assertEqual(output["data"], reordered["data"])
        result, _ = self.run_helper([a, b], legacy=True)
        self.assertEqual(result.returncode, 3)
        self.assertEqual(result.stdout, "")
        self.assertIn("BRANCH_UNION_UNCERTAIN", result.stderr)

    def test_condition_identity_unions_complementary_complete_groups(self):
        reports = []
        for index in range(2):
            path = self.report(f"{index}.xml", branch=(2, 4))
            xml = ET.parse(path)
            for line in xml.findall(".//line[@branch='true']"):
                conditions = ET.SubElement(line, "conditions")
                for number in range(2):
                    ET.SubElement(conditions, "condition", number=str(number), type="jump",
                                  coverage="100%" if number == index else "0%")
            xml.write(path, encoding="utf-8")
            reports.append(path)
        result, output = self.run_helper(reports)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(output["data"]["methods"][0]["BranchCoverage"], 100)

    def test_condition_identity_does_not_identify_partial_outcome(self):
        reports = []
        for name in ("a.xml", "b.xml"):
            path = self.report(name, branch=(1, 2))
            xml = ET.parse(path)
            for line in xml.findall(".//line[@branch='true']"):
                ET.SubElement(ET.SubElement(line, "conditions"), "condition", number="0", type="jump", coverage="50%")
            xml.write(path, encoding="utf-8")
            reports.append(path)
        _, output = self.run_helper(reports)
        self.assertEqual(output["data"]["methods"][0]["BranchCoverageStatus"], "bounded")

    def test_conflicting_branch_universes_are_unknown(self):
        _, output = self.run_helper([
            self.report("a.xml", branch=(1, 2)), self.report("b.xml", branch=(1, 4)),
        ])
        method = output["data"]["methods"][0]
        self.assertEqual(method["BranchCoverageStatus"], "unknown")
        self.assertIsNone(method["TotalBranches"])

    def test_aggregate_uses_same_source_line_union_not_counter_sum(self):
        a = self.report("a.xml", hits=(1, 0))
        b = self.report("b.xml", hits=(0, 1))
        result, output = self.run_helper([a, b], script="Compute-CrapScores.ps1")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(output["data"]["summary"]["overall_line_coverage"], 100)
        self.assertEqual(output["data"]["methods"][0]["LineCoverage"], 100)
        self.assertEqual(output["data"]["methods"][0]["CrapScore"], 2)

    def test_overlapping_counter_only_reports_do_not_claim_exact_aggregate(self):
        paths = [self.report("a.xml", class_lines=False), self.report("b.xml", class_lines=False)]
        _, output = self.run_helper(paths, script="Compute-CrapScores.ps1")
        self.assertIsNone(output["data"]["summary"]["overall_line_coverage"])
        self.assertEqual(output["data"]["summary"]["coverage_scope"], "method-lines-only")
        self.assertEqual(output["status"], "partial")

    def test_missing_and_conflicting_complexity_do_not_produce_crap(self):
        for paths in (
            [self.report("missing.xml", complexity=None)],
            [self.report("a.xml", complexity="2"), self.report("b.xml", complexity="3")],
        ):
            with self.subTest(paths=paths):
                _, output = self.run_helper(paths, script="Compute-CrapScores.ps1")
                self.assertIsNone(output["data"]["methods"][0]["Complexity"])
                self.assertIsNone(output["data"]["methods"][0]["CrapScore"])
                self.assertEqual(output["data"]["summary"]["unknown_crap_methods"], 1)
                self.assertEqual(output["data"]["hotspots"], [])

    def test_empty_method_lines_are_unknown_not_uncovered(self):
        _, output = self.run_helper([self.report("empty.xml", hits=())], script="Compute-CrapScores.ps1")
        self.assertIsNone(output["data"]["methods"][0]["LineCoverage"])
        self.assertIsNone(output["data"]["methods"][0]["CrapScore"])
        self.assertEqual(output["status"], "partial")

    def test_invalid_xml_numbers_and_report_roots_fail(self):
        bad = self.workspace / "bad.xml"
        for content in ("<coverage>", "<unrelated/>"):
            bad.write_text(content)
            result, output = self.run_helper([bad])
            self.assertEqual(result.returncode, 2)
            self.assertEqual(output["status"], "error")
        for complexity in ("0", "-1", "NaN"):
            with self.subTest(complexity=complexity):
                result, _ = self.run_helper([self.report("invalid.xml", complexity=complexity)])
                self.assertEqual(result.returncode, 2)
        result, _ = self.run_helper([self.workspace / "missing.xml"])
        self.assertEqual(result.returncode, 2)

    def test_missing_branch_metadata_and_missing_methods_are_partial(self):
        path = self.report("incomplete.xml", branch=(1, 2))
        xml = ET.parse(path)
        for line in xml.findall(".//line[@branch='true']"):
            del line.attrib["condition-coverage"]
        xml.write(path)
        _, output = self.run_helper([path])
        self.assertEqual(output["data"]["methods"][0]["BranchCoverageStatus"], "unknown")
        complete = self.report("complete.xml", branch=(1, 2))
        _, output = self.run_helper([path, complete])
        self.assertEqual(output["data"]["methods"][0]["BranchCoverageStatus"], "unknown")
        path.write_text("<coverage/>")
        _, output = self.run_helper([path])
        self.assertEqual(output["status"], "partial")
        self.assertEqual(output["data"]["methods"], [])

    def test_thresholds_compare_unrounded_evidence(self):
        path = self.report("rounding.xml", hits=(1,) * 1600 + (0,) * 401)
        _, output = self.run_helper([path], extra="-Filter below-threshold -CoverageThreshold 80")
        self.assertEqual(len(output["data"]["methods"]), 1)
        self.assertEqual(output["data"]["methods"][0]["LineCoverage"], 80)
        path = self.report("branches.xml", hits=(1, 1), branch=(6999, 10000))
        _, output = self.run_helper([path], extra="-Filter below-threshold -BranchThreshold 70")
        self.assertEqual(len(output["data"]["methods"]), 1)
        self.assertEqual(output["data"]["methods"][0]["BranchCoverage"], 70)

    def test_crap_threshold_uses_raw_score_but_displays_rounded_value(self):
        cases = (
            ("above.xml", (1,) * 23 + (0,) * 159, "6", 1),
            ("equal.xml", (0,), "5", 0),
        )
        for name, hits, complexity, flagged in cases:
            with self.subTest(name=name):
                path = self.report(name, hits=hits, complexity=complexity)
                result, output = self.run_helper([path], script="Compute-CrapScores.ps1")
                self.assertEqual(result.returncode, 0, result.stderr)
                self.assertEqual(output["data"]["methods"][0]["CrapScore"], 30)
                self.assertEqual(output["data"]["summary"]["flagged_methods"], flagged)
                result, _ = self.run_helper([path], script="Compute-CrapScores.ps1", legacy=True)
                self.assertEqual(result.returncode, 0, result.stderr)
                markers = dict(line.split(":", 1) for line in result.stdout.splitlines())
                self.assertEqual(markers["FLAGGED_METHODS"], str(flagged))
                self.assertEqual(json.loads(markers["HOTSPOTS"])[0]["CrapScore"], 30)

    def test_crap_hotspots_rank_raw_scores_before_truncating(self):
        exact = self.report("exact.xml", hits=(0,), complexity="5", signature="(a)")
        above = self.report("above.xml", hits=(1,) * 23 + (0,) * 159, complexity="6", signature="(z)")
        _, output = self.run_helper([exact, above], script="Compute-CrapScores.ps1", extra="-TopN 1")
        self.assertEqual(output["data"]["summary"]["flagged_methods"], 1)
        self.assertEqual(len(output["data"]["hotspots"]), 1)
        self.assertEqual(output["data"]["hotspots"][0]["Signature"], "(z)")
        self.assertEqual(output["data"]["hotspots"][0]["CrapScore"], 30)

    def test_legacy_empty_single_and_multiple_arrays_are_json_only(self):
        complete = self.report("complete.xml", hits=(1, 1))
        result, _ = self.run_helper([complete], legacy=True, extra="-Filter uncovered")
        self.assertEqual(json.loads(result.stdout), [])
        result, _ = self.run_helper([complete], legacy=True)
        self.assertEqual(len(json.loads(result.stdout)), 1)
        self.assertIn("METHODS_FILTERED:1", result.stderr)
        result, _ = self.run_helper([EXISTING], legacy=True)
        self.assertEqual(len(json.loads(result.stdout)), 4)

    def test_legacy_crap_markers_and_fractional_arithmetic_are_preserved(self):
        path = self.report("half.xml")
        result, _ = self.run_helper([path], script="Compute-CrapScores.ps1", legacy=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        markers = dict(line.split(":", 1) for line in result.stdout.splitlines())
        self.assertEqual(markers["OVERALL_LINE_COVERAGE"], "50")
        self.assertEqual(json.loads(markers["HOTSPOTS"])[0]["CrapScore"], 2.5)
        _, output = self.run_helper([path], script="Compute-CrapScores.ps1", culture="de-DE")
        self.assertEqual(output["data"]["methods"][0]["CrapScore"], 2.5)

    def test_overloads_are_distinct(self):
        _, output = self.run_helper([self.report("a.xml", signature="(int)"), self.report("b.xml", signature="(str)")])
        self.assertEqual(len(output["data"]["methods"]), 2)

    def test_ambiguous_duplicate_method_identity_is_not_silently_merged(self):
        path = self.report("ambiguous.xml", signature="")
        xml = ET.parse(path)
        methods = xml.find(".//methods")
        methods.append(ET.fromstring(ET.tostring(methods[0])))
        xml.write(path)
        result, output = self.run_helper([path])
        self.assertEqual(result.returncode, 2)
        self.assertIn("ambiguous", output["diagnostics"][0]["message"])

    def test_missing_or_invalid_adjacent_helper_is_dependency_error(self):
        installed = self.workspace / "incomplete install"
        installed.mkdir()
        for name in ("Extract-MethodCoverage.ps1", "Compute-CrapScores.ps1"):
            script = installed / name
            shutil.copyfile(SCRIPTS / name, script)
            for corrupt in (False, True):
                with self.subTest(script=name, corrupt=corrupt):
                    dependency = installed / "CoverageData.ps1"
                    if corrupt:
                        dependency.write_text("function Broken {", encoding="utf-8")
                    result, output = self.run_helper([EXISTING], script=str(script))
                    self.assertEqual(result.returncode, 2)
                    self.assertEqual(output["diagnostics"][0]["code"], "DEPENDENCY_UNAVAILABLE")
                    self.assertIn("DEPENDENCY_UNAVAILABLE", result.stderr)
                    if corrupt:
                        dependency.unlink()


if __name__ == "__main__":
    unittest.main()
