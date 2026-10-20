import unittest
from pathlib import Path

from fixture_validation import validate_text


FIXTURES = Path(__file__).parent / "fixtures"
KNOWN = {
    "mt-fod-launched.txt": "fod-launched",
    "mt-fod-suppressed.txt": "fod-suppressed",
    "csc-healthy.txt": "healthy-managed-v4",
    "link-multi-activation.txt": "multi-activation-capped-failure",
    "link-fod-suppressed.txt": "multi-activation-capped-failure",
    "tool-with-legacy-policy.txt": "legacy-policy-v4-success",
    "wrong-runtime-order.txt": "wrong-runtime-v2-selected",
    "order-legacy-first.txt": "legacy-bound-before-com",
    "order-com-first.txt": "com-before-legacy-bind",
    "hresult-runtime-load-failure.txt": "explicit-v2-runtime-missing",
    "modern-hostfxr.txt": "modern-hostfxr",
    "fusion-bind-failure.txt": "fusion-bind-failure",
}


class FixtureValidationTests(unittest.TestCase):
    def test_known_fixtures_are_accepted(self) -> None:
        for filename, expected in KNOWN.items():
            with self.subTest(filename=filename):
                text = (FIXTURES / filename).read_text(encoding="utf-8")
                self.assertEqual([], validate_text(text, expected))

    def test_launched_log_mutated_to_suppressed_is_rejected(self) -> None:
        text = (FIXTURES / "mt-fod-launched.txt").read_text(encoding="utf-8")
        mutated = text.replace(
            "Launching feature-on-demand installation",
            "Could have launched feature-on-demand installation",
        )
        self.assertTrue(validate_text(mutated, "fod-launched"))

    def test_wrong_runtime_log_mutated_to_v4_is_rejected(self) -> None:
        text = (FIXTURES / "wrong-runtime-order.txt").read_text(encoding="utf-8")
        mutated = text.replace(
            "Using supportedRuntime: v2.0.50727",
            "Using supportedRuntime: v4.0.30319",
        )
        self.assertTrue(validate_text(mutated, "wrong-runtime-v2-selected"))

    def test_known_log_with_wrong_classification_is_rejected(self) -> None:
        text = (FIXTURES / "csc-healthy.txt").read_text(encoding="utf-8")
        self.assertTrue(validate_text(text, "fod-launched"))

    def test_non_clr_log_is_rejected_as_activation_log(self) -> None:
        text = (FIXTURES / "modern-hostfxr.txt").read_text(encoding="utf-8")
        self.assertTrue(validate_text(text, "healthy-managed-v4"))

    def test_patch_marker_in_fusion_log_is_rejected(self) -> None:
        text = (FIXTURES / "fusion-bind-failure.txt").read_text(encoding="utf-8")
        mutated = text + "\n*** End Patch\n"
        self.assertTrue(validate_text(mutated, "fusion-bind-failure"))

    def test_truncated_order_log_is_rejected(self) -> None:
        text = (FIXTURES / "order-legacy-first.txt").read_text(encoding="utf-8")
        mutated = text.replace(
            "Legacy runtime is now bound to: v4.0.30319\n", ""
        )
        self.assertTrue(validate_text(mutated, "legacy-bound-before-com"))


if __name__ == "__main__":
    unittest.main()
