#!/usr/bin/env python3
"""Focused regression tests for the persistent JavaScript regex worker."""

from concurrent.futures import ThreadPoolExecutor, TimeoutError as FutureTimeoutError
import importlib.util
import os
import pathlib
import re
import subprocess
import sys
import threading
import time
import types
import unittest
from unittest import mock


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
        self.assertTrue(gate.vally_regex_found("tail$", ("x" * 2_000_000) + "tail"))

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
        # The restart includes Node cold start, which can exceed 0.1 seconds on Windows.
        worker._timeout = gate.JAVASCRIPT_REGEX_TIMEOUT_SECONDS
        self.assertTrue(worker.test("Thing", 0, "Thing"))
        worker.close()

    def test_timeout_covers_blocked_request_write(self):
        worker = gate._JavaScriptRegexWorker(
            script='setTimeout(() => process.exit(0), 3000);', timeout=0.5)
        try:
            worker._start_locked()
            process = worker._process
            started = time.monotonic()
            with self.assertRaisesRegex(re.error, "JavaScript regex validation timed out"):
                worker.test("Thing", 0, "x" * 2_000_000)
            self.assertLess(time.monotonic() - started, 2.5)
            self.assertIsNone(worker._process)
            self.assertIsNotNone(process.poll())
            worker._script = gate.JAVASCRIPT_REGEX_SCRIPT
            worker._timeout = gate.JAVASCRIPT_REGEX_TIMEOUT_SECONDS
            self.assertTrue(worker.test("Thing", 0, "Thing"))
        finally:
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
        cases = [(f"^{index}$", str(index if index % 2 == 0 else index + 1))
                 for index in range(16)]
        with ThreadPoolExecutor(max_workers=8) as executor:
            results = list(executor.map(
                lambda item: gate.vally_regex_found(*item),
                cases))
        self.assertEqual([index % 2 == 0 for index in range(16)], results)

    def test_queued_call_gets_full_execution_timeout(self):
        worker = gate._JavaScriptRegexWorker(script=r'''
const lines = require("readline").createInterface({ input: process.stdin });
lines.on("line", line => {
    const request = JSON.parse(line);
    setTimeout(() => process.stdout.write(JSON.stringify({
        id: request.id, ok: true, result: true
    }) + "\n"), 500);
});
''')
        entered = threading.Event()
        lock = worker._lock

        class ObservedLock:
            def acquire(self, timeout=-1):
                entered.set()
                return lock.acquire(timeout=timeout)

            def release(self):
                lock.release()

            def __enter__(self):
                lock.acquire()
                return self

            def __exit__(self, *args):
                lock.release()

        try:
            self.assertTrue(worker.test("Thing", 0, "Thing"))
            process = worker._process
            lock.acquire()
            worker._lock = ObservedLock()
            offset = [0.0]
            clock = types.SimpleNamespace(monotonic=lambda: time.monotonic() + offset[0])
            with mock.patch.object(gate, "time", clock):
                with ThreadPoolExecutor(max_workers=1) as executor:
                    future = executor.submit(worker.test, "Thing", 0, "Thing")
                    try:
                        self.assertTrue(entered.wait(timeout=2))
                        offset[0] = 4.8
                    finally:
                        lock.release()
                    self.assertTrue(future.result(timeout=3))
            self.assertIs(process, worker._process)
        finally:
            worker.close()

    def test_queued_call_can_wait_longer_than_execution_timeout(self):
        worker = gate._JavaScriptRegexWorker()
        try:
            self.assertTrue(worker.test("Thing", 0, "Thing"))
            process = worker._process
            worker._timeout = 1
            lock = worker._lock
            entered = threading.Event()

            class ObservedLock:
                def acquire(self, *args, **kwargs):
                    entered.set()
                    return lock.acquire(*args, **kwargs)

                def release(self):
                    lock.release()

                def __enter__(self):
                    lock.acquire()
                    return self

                def __exit__(self, *args):
                    lock.release()

            lock.acquire()
            worker._lock = ObservedLock()
            with ThreadPoolExecutor(max_workers=1) as executor:
                future = executor.submit(worker.test, "Thing", 0, "Thing")
                try:
                    self.assertTrue(entered.wait(timeout=2))
                    with self.assertRaises(FutureTimeoutError):
                        future.result(timeout=worker._timeout + 0.5)
                finally:
                    lock.release()
                self.assertTrue(future.result(timeout=3))
            self.assertIs(process, worker._process)
        finally:
            worker.close()

    def test_inherited_worker_guard_does_not_touch_process_or_lock(self):
        worker = gate._JavaScriptRegexWorker()
        worker._owner_pid = -1
        worker._process = mock.Mock()
        worker._lock = mock.MagicMock()
        with self.assertRaisesRegex(re.error, "cannot be used after fork"):
            worker.test("x", 0, "x")
        worker.close()
        worker._lock.acquire.assert_not_called()
        worker._lock.__enter__.assert_not_called()
        worker._process.stdin.close.assert_not_called()
        worker._process.stdout.close.assert_not_called()
        worker._process.stderr.close.assert_not_called()

    @unittest.skipUnless(hasattr(os, "fork"), "requires POSIX fork")
    def test_forked_child_rejects_inherited_worker_without_touching_pipes(self):
        program = r'''
import importlib.util, os, re, signal, sys, time
spec = importlib.util.spec_from_file_location("gate", sys.argv[1])
gate = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = gate
spec.loader.exec_module(gate)
worker = gate._JavaScriptRegexWorker()
assert worker.test("x", 0, "x")
pid = os.fork()
if pid == 0:
    try:
        worker.test("x", 0, "x")
    except re.error as exc:
        worker.close()
        os._exit(0 if "cannot be used after fork" in str(exc) else 1)
    os._exit(2)
deadline = time.monotonic() + 5
while True:
    result, status = os.waitpid(pid, os.WNOHANG)
    if result:
        break
    if time.monotonic() >= deadline:
        os.kill(pid, signal.SIGKILL)
        os.waitpid(pid, 0)
        raise AssertionError("forked child blocked")
    time.sleep(0.01)
assert os.waitstatus_to_exitcode(status) == 0
assert worker.test("x", 0, "x")
worker.close()
'''
        result = subprocess.run(
            [sys.executable, "-c", program, str(MODULE_PATH)],
            capture_output=True, text=True, timeout=15)
        self.assertEqual(0, result.returncode, result.stderr)

    def test_close_reaps_the_worker(self):
        worker = gate._JavaScriptRegexWorker()
        self.assertTrue(worker.test("Thing", 0, "Thing"))
        process = worker._process
        worker.close()
        self.assertIsNone(worker._process)
        self.assertIsNotNone(process.poll())


if __name__ == "__main__":
    unittest.main()
