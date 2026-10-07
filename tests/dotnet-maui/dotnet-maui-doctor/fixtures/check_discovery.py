import json
import subprocess
import sys
from pathlib import Path

from check_state import check


def run(set_path, catalog_path, output_path):
    return subprocess.run(
        [sys.executable, "resolve_requirements.py", "--set", str(set_path),
         "--catalog", str(catalog_path), "--output", str(output_path)],
        capture_output=True, text=True, timeout=10,
    )


def expected(band, version, packages, jdk):
    return {
        "manifestPackage": f"microsoft.net.sdk.android.manifest-{band}",
        "manifestVersion": version,
        "manifestFeatureBand": band,
        "jdkRange": "[17.0,22.0)",
        "jdkRecommendedVersion": jdk,
        "packages": sorted(packages),
    }


if __name__ == "__main__":
    result = run("inputs/workloadset.json", "inputs/catalog.json", "requirements.json")
    assert result.returncode == 0, result.stderr
    assert json.loads(Path("requirements.json").read_text()) == expected(
        "9.0.100", "35.0.50",
        ["build-tools;35.0.0", "cmdline-tools;12.0", "platforms;android-35", "platform-tools"],
        "17.0.12",
    ), "Wrong manifest band, requirements or optional-package selection"
    check(("resolve_requirements.py", "requirements.json"))
    variant_set = Path(".variant-set.json")
    variant_catalog = Path(".variant-catalog.json")
    variant_output = Path(".variant-output.json")
    try:
        variant_set.write_text(json.dumps({"microsoft.net.sdk.android": "35.0.60/9.0.200"}))
        variant_catalog.write_text(json.dumps({
            "microsoft.net.sdk.android.manifest-9.0.200/35.0.60": {
                "microsoft.net.sdk.android": {
                    "jdk": {"version": "[17.0,22.0)", "recommendedVersion": "17.0.14"},
                    "androidsdk": {"packages": [
                        {"sdkPackage": {"id": "platforms;android-36"}, "optional": False},
                        {"sdkPackage": {"id": "emulator"}, "optional": True},
                    ]},
                },
            },
        }))
        result = run(variant_set, variant_catalog, variant_output)
        assert result.returncode == 0, result.stderr
        assert json.loads(variant_output.read_text()) == expected(
            "9.0.200", "35.0.60", ["platforms;android-36"], "17.0.14",
        ), "Hardcoded fixture answer or incorrect boolean handling"
        for broken in ({}, {"Microsoft.NET.Sdk.Android": "35.0.50"},
                       {"Microsoft.NET.Sdk.Android": "35.0.50/9.0.100/extra"},
                       {"Microsoft.NET.Sdk.Android": "99.0.0/99.0.100"}):
            variant_set.write_text(json.dumps(broken))
            variant_output.unlink(missing_ok=True)
            result = run(variant_set, variant_catalog, variant_output)
            assert result.returncode != 0, "Missing/malformed metadata must fail closed"
            assert not variant_output.exists(), "Failed discovery produced misleading output"
    finally:
        for path in (variant_set, variant_catalog, variant_output):
            path.unlink(missing_ok=True)
    print("Offline discovery passed: exact band, required packages, variants and missing metadata")
