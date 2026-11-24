#!/usr/bin/env python3
"""Focused regression tests for the persistent JavaScript regex worker."""

from concurrent.futures import ThreadPoolExecutor
import importlib.util
import pathlib
import re
import sys
import unittest


MODULE_PATH = pathlib.Path(__file__).with_name("check_eval_quality.py")
SPEC = importlib.util.spec_from_file_location("check_eval_quality_for_worker_test", MODULE_PATH)
assert SPEC is not None and SPEC.loader is not None
gate = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = gate
SPEC.loader.exec_module(gate)


class JavaScriptRegexWorkerTests(unittest.TestCase):
    def tearDown(self):
        gate._javascript_regex_worker.close()
        gate.validate_vally_regex.cache_clear()

    def test_worker_is_reused_and_preserves_javascript_semantics(self):
        self.assertTrue(gate.vally_regex_found(r"(?im)^thing$", "Before\nThing\nAfter"))
        process = gate._javascript_regex_worker._process
        self.assertIsNotNone(process)
        self.assertFalse(gate.vally_regex_found(r"(?<word>Thing)\s+\k<word>", "Thing Other"))
        self.assertEqual(process.pid, gate._javascript_regex_worker._process.pid)
        self.assertTrue(gate.vally_regex_found("cafe\u0301", "cafe\u0301"))
        self.assertTrue(gate.vally_regex_found("\ud800", "\ud800"))
        self.assertTrue(gate.vally_regex_found("tail$", ("x" * 100_000) + "tail"))

    def test_invalid_pattern_does_not_poison_worker(self):
        with self.assertRaisesRegex(re.error, "Invalid regular expression"):
            gate.vally_regex_found("[", "anything")
        process = gate._javascript_regex_worker._process
        self.assertTrue(gate.vally_regex_found("Thing", "Thing"))
        self.assertEqual(process.pid, gate._javascript_regex_worker._process.pid)

    def test_missing_node_keeps_existing_diagnostic(self):
        worker = gate._JavaScriptRegexWorker(
            command="node-command-that-does-not-exist-for-regex-worker-test")
        with self.assertRaisesRegex(
                re.error, "Node.js is required to validate Vally JavaScript regexes"):
            worker.test("Thing", 0, "Thing")

    def test_node_startup_failure_surfaces_stderr(self):
        worker = gate._JavaScriptRegexWorker(
            script='process.stderr.write("unsupported Node version"); process.exit(2);')
        with self.assertRaisesRegex(re.error, "unsupported Node version"):
            worker.test("Thing", 0, "Thing")
        worker.close()

    def test_timeout_resets_worker_and_next_request_succeeds(self):
        worker = gate._JavaScriptRegexWorker(
            script='process.stdin.resume();', timeout=0.1)
        with self.assertRaisesRegex(re.error, "JavaScript regex validation timed out"):
            worker.test("Thing", 0, "Thing")
        self.assertIsNone(worker._process)
        worker._script = gate.JAVASCRIPT_REGEX_SCRIPT
        self.assertTrue(worker.test("Thing", 0, "Thing"))
        worker.close()

    def test_protocol_corruption_resets_worker(self):
        worker = gate._JavaScriptRegexWorker(
            script='process.stdin.once("data", () => process.stdout.write("not-json\\n"));')
        with self.assertRaisesRegex(re.error, "returned an invalid response"):
            worker.test("Thing", 0, "Thing")
        self.assertIsNone(worker._process)
        worker._script = gate.JAVASCRIPT_REGEX_SCRIPT
        self.assertTrue(worker.test("Thing", 0, "Thing"))
        worker.close()

    def test_concurrent_callers_receive_their_own_results(self):
        cases = [(f"^{index}$", str(index)) for index in range(16)]
        with ThreadPoolExecutor(max_workers=8) as executor:
            results = list(executor.map(
                lambda item: gate.vally_regex_found(*item),
                cases))
        self.assertEqual([True] * len(cases), results)

    def test_close_reaps_the_worker(self):
        worker = gate._JavaScriptRegexWorker()
        self.assertTrue(worker.test("Thing", 0, "Thing"))
        process = worker._process
        worker.close()
        self.assertIsNone(worker._process)
        self.assertIsNotNone(process.poll())


if __name__ == "__main__":
    unittest.main()
