import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path


MODULE_PATH = Path(__file__).with_name("copilot_pin.py")
SPEC = importlib.util.spec_from_file_location("copilot_pin", MODULE_PATH)
assert SPEC and SPEC.loader
copilot_pin = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = copilot_pin
SPEC.loader.exec_module(copilot_pin)


class CopilotPinTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.workflows = self.root / ".github" / "workflows"
        self.workflows.mkdir(parents=True)
        self.compat = self.root / "compat.json"
        self.compat.write_text(
            json.dumps(
                {
                    "blockedVersions": [],
                    "minimumVersion": "v0.65.3",
                    "agent-compat-v1": {
                        "copilot": [
                            {
                                "min-gh-aw": "0.72.0",
                                "max-gh-aw": "*",
                                "min-agent": "1.0.21",
                                "max-agent": "1.0.85",
                                "open": True,
                            }
                        ]
                    },
                }
            ),
            encoding="utf-8",
        )
        for workflow_id in copilot_pin.WORKFLOW_IDS:
            (self.workflows / f"{workflow_id}.md").write_text(
                "---\nengine:\n  id: copilot\n  version: 1.0.80\n---\n",
                encoding="utf-8",
            )
            metadata = {
                "compiler_version": "v0.88.7",
                "strict": True,
                "engine_versions": {"copilot": "1.0.80"},
            }
            manifest = {
                "actions": [
                    {
                        "repo": "github/gh-aw-actions/setup",
                        "sha": "5e508589e03a7757a7e05b26e834292f5445bfb6",
                    }
                ],
                "containers": [
                    {
                        "image": "ghcr.io/github/gh-aw-mcpg:v0.4.18",
                        "pinned_image": (
                            "ghcr.io/github/gh-aw-mcpg:v0.4.18@sha256:"
                            + "a" * 64
                        ),
                    }
                ],
            }
            (self.workflows / f"{workflow_id}.lock.yml").write_text(
                f"# gh-aw-metadata: {json.dumps(metadata)}\n"
                f"# gh-aw-manifest: {json.dumps(manifest)}\n",
                encoding="utf-8",
            )

    def test_plans_from_owned_compatibility_and_lock_manifest(self) -> None:
        plan = copilot_pin.build_plan(self.root, self.compat)

        self.assertEqual(plan.compiler, "v0.88.7")
        self.assertEqual(plan.current, "1.0.80")
        self.assertEqual(plan.candidate, "1.0.85")
        self.assertTrue(plan.stale)
        self.assertEqual(
            plan.gateway_image,
            "ghcr.io/github/gh-aw-mcpg:v0.4.18@sha256:" + "a" * 64,
        )

    def test_applies_same_candidate_to_all_authoritative_sources(self) -> None:
        plan = copilot_pin.build_plan(self.root, self.compat)

        copilot_pin.apply_candidate(self.root, plan)

        for workflow_id in copilot_pin.WORKFLOW_IDS:
            text = (self.workflows / f"{workflow_id}.md").read_text(
                encoding="utf-8"
            )
            self.assertIn("version: 1.0.85", text)
            self.assertNotIn("version: 1.0.80", text)

    def test_rejects_mixed_compiler_ownership(self) -> None:
        path = self.workflows / "devops-health-groom.lock.yml"
        text = path.read_text(encoding="utf-8").replace("v0.88.7", "v0.87.0")
        path.write_text(text, encoding="utf-8")

        with self.assertRaisesRegex(copilot_pin.PinError, "compiler version"):
            copilot_pin.build_plan(self.root, self.compat)

    def test_rejects_gateway_without_digest(self) -> None:
        path = self.workflows / "devops-health-check.lock.yml"
        text = path.read_text(encoding="utf-8").replace(
            "ghcr.io/github/gh-aw-mcpg:v0.4.18@sha256:" + "a" * 64,
            "ghcr.io/github/gh-aw-mcpg:v0.4.18",
        )
        path.write_text(text, encoding="utf-8")

        with self.assertRaisesRegex(copilot_pin.PinError, "digest-pinned"):
            copilot_pin.build_plan(self.root, self.compat)

    def test_rejects_candidate_older_than_explicit_pin(self) -> None:
        compat = json.loads(self.compat.read_text(encoding="utf-8"))
        compat["agent-compat-v1"]["copilot"][0]["max-agent"] = "1.0.79"
        self.compat.write_text(json.dumps(compat), encoding="utf-8")

        with self.assertRaisesRegex(copilot_pin.PinError, "newer than"):
            copilot_pin.build_plan(self.root, self.compat)


if __name__ == "__main__":
    unittest.main()
