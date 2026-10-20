"""Validate the diagnostic shape of the checked-in activation fixtures."""

from __future__ import annotations

import argparse
from pathlib import Path


REQUIRED: dict[str, tuple[str, ...]] = {
    "fod-launched": (
        "CLR Loading log for",
        "FunctionCall: DllGetClassObject",
        "IsLegacyBind is: 1",
        "IsCapped is 1",
        "Config File (Open). Result:80070002",
        "SEM_FAILCRITICALERRORS is set to 0",
        "Launching feature-on-demand installation",
    ),
    "fod-suppressed": (
        "CLR Loading log for",
        "FunctionCall: DllGetClassObject",
        "IsCapped is 1",
        "SEM_FAILCRITICALERRORS is set to 32769",
        "Could have launched feature-on-demand installation",
    ),
    "healthy-managed-v4": (
        "CLR Loading log for",
        "FunctionCall: _CorExeMain",
        "IsLegacyBind is: 0",
        "Decided on runtime: v4.0.30319",
        "Runtime has been loaded.  Version: v4.0.30319",
    ),
    "multi-activation-capped-failure": (
        "CLR Loading log for",
        "FunctionCall: ClrCreateInstance",
        "Runtime has been loaded.  Version: v4.0.30319",
        "FunctionCall: DllGetClassObject",
        "ERROR: Unable to find a version of the runtime to use.",
    ),
    "legacy-policy-v4-success": (
        "CLR Loading log for",
        "UseLegacyV2RuntimeActivationPolicy is set to 1",
        "FunctionCall: DllGetClassObject",
        "IsCapped is 1",
        "Decided on runtime: v4.0.30319",
    ),
    "wrong-runtime-v2-selected": (
        "CLR Loading log for",
        "Installed Runtime: v2.0.50727",
        "Installed Runtime: v4.0.30319",
        "Config file includes SupportedRuntime entry.  Version: v2.0.50727",
        "Config file includes SupportedRuntime entry.  Version: v4.0.30319",
        "Using supportedRuntime: v2.0.50727",
        "Runtime has been loaded.  Version: v2.0.50727",
    ),
    "legacy-bound-before-com": (
        "CLR Loading log for",
        "LegacyFunctionCall: CorBindToRuntimeEx.  Version: v4.0.30319",
        "Legacy runtime is now bound to: v4.0.30319",
        "FunctionCall: DllGetClassObject",
        "Using already bound legacy runtime: v4.0.30319",
        "COM activation completed using runtime: v4.0.30319",
    ),
    "com-before-legacy-bind": (
        "CLR Loading log for",
        "FunctionCall: DllGetClassObject",
        "ERROR: Unable to find a version of the runtime to use.",
        "LegacyFunctionCall: CorBindToRuntimeEx.  Version: v4.0.30319",
        "Legacy runtime is now bound to: v4.0.30319",
    ),
    "explicit-v2-runtime-missing": (
        "CLR Loading log for",
        "LegacyFunctionCall: CorBindToRuntimeEx.  Version: v2.0.50727",
        "Installed Runtime: v4.0.30319",
        "Requested runtime is not installed: v2.0.50727",
        "ERROR: Unable to find a version of the runtime to use.",
    ),
    "modern-hostfxr": (
        "Tracing enabled @",
        "hostfxr_resolve_sdk2",
        "The framework 'Microsoft.NETCore.App'",
        "framework_version=8.0.0",
    ),
    "fusion-bind-failure": (
        "*** Assembly Binder Log Entry",
        "LOG: This bind starts in default load context.",
        "ERR: Failed to complete setup of assembly",
        "System.IO.FileNotFoundException",
        "LOG: All probing URLs attempted and failed.",
    ),
}

FORBIDDEN: dict[str, tuple[str, ...]] = {
    "fod-launched": ("Could have launched feature-on-demand",),
    "fod-suppressed": ("Launching feature-on-demand installation",),
    "healthy-managed-v4": ("ERROR:", "feature-on-demand"),
    "wrong-runtime-v2-selected": ("Using supportedRuntime: v4.0.30319",),
    "legacy-bound-before-com": ("ERROR:",),
    "modern-hostfxr": ("CLR Loading log for",),
    "fusion-bind-failure": ("CLR Loading log for", "*** End Patch"),
}


def validate_text(text: str, expected: str) -> list[str]:
    if expected not in REQUIRED:
        return [f"unknown expected classification: {expected}"]
    errors = [
        f"missing required marker: {marker}"
        for marker in REQUIRED[expected]
        if marker not in text
    ]
    errors.extend(
        f"contains forbidden marker: {marker}"
        for marker in FORBIDDEN.get(expected, ())
        if marker in text
    )
    return errors


def validate_file(path: Path, expected: str) -> None:
    errors = validate_text(path.read_text(encoding="utf-8"), expected)
    if errors:
        joined = "\n  - ".join(errors)
        raise ValueError(f"{path} is not {expected}:\n  - {joined}")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("path", type=Path)
    parser.add_argument("expected", choices=sorted(REQUIRED))
    args = parser.parse_args()
    validate_file(args.path, args.expected)
    print(f"{args.path}: valid {args.expected}")


if __name__ == "__main__":
    main()
