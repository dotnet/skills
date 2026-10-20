from __future__ import annotations

import hashlib
import re
import shutil
import subprocess
import sys
from collections import Counter
from pathlib import Path


NOOP_HASHES = {
    "DetailsPage.cs": "d0dc3bbcc02ca56a736139fe186e7d800fc7dd78343f1221527b5e7566f802d5",
    "MauiProgram.cs": "b459ceee8eb2d23c6bd9f855135eddeb91b45f3e127f4d78db920fff769d3eb1",
}
EXPECTED_REGISTRATIONS = {
    "IInventoryService": ("Singleton", ("IInventoryService", "InventoryService")),
    "InventoryViewModel": ("Transient", ("InventoryViewModel",)),
    "InventoryPage": ("Transient", ("InventoryPage",)),
    "ItemDetailsViewModel": ("Transient", ("ItemDetailsViewModel",)),
    "ItemDetailsPage": ("Transient", ("ItemDetailsPage",)),
}
REGISTRATION = re.compile(
    r"builder\s*\.\s*Services\s*\.\s*Add"
    r"(?P<lifetime>Singleton|Transient|Scoped)\s*"
    r"<(?P<types>[^<>]+)>\s*\(\s*\)\s*;"
)


def fail(message: str) -> None:
    raise SystemExit(message)


def file_set(root: Path) -> list[str]:
    return sorted(
        path.relative_to(root).as_posix()
        for path in root.rglob("*")
        if path.is_file()
    )


def strip_comments(text: str) -> str:
    result: list[str] = []
    index = 0
    state = "code"
    while index < len(text):
        char = text[index]
        next_char = text[index + 1] if index + 1 < len(text) else ""
        if state == "code":
            if char == "/" and next_char == "/":
                result.extend("  ")
                index += 2
                state = "line-comment"
                continue
            if char == "/" and next_char == "*":
                result.extend("  ")
                index += 2
                state = "block-comment"
                continue
            if char == '"':
                state = "string"
            elif char == "'":
                state = "char"
            result.append(char)
            index += 1
            continue
        if state == "line-comment":
            if char == "\n":
                result.append("\n")
                state = "code"
            else:
                result.append(" ")
            index += 1
            continue
        if state == "block-comment":
            if char == "*" and next_char == "/":
                result.extend("  ")
                index += 2
                state = "code"
            else:
                result.append("\n" if char == "\n" else " ")
                index += 1
            continue
        result.append(char)
        if char == "\\":
            if index + 1 < len(text):
                result.append(text[index + 1])
                index += 2
            else:
                index += 1
        elif state == "string" and char == '"':
            state = "code"
            index += 1
        elif state == "char" and char == "'":
            state = "code"
            index += 1
        else:
            index += 1
    if state == "block-comment":
        fail("Unterminated block comment")
    return "".join(result)


def mask_literals(text: str) -> str:
    masked = list(text)
    index = 0
    while index < len(text):
        prefix_length = 0
        verbatim = False
        delimiter = ""
        if text.startswith('$@"', index) or text.startswith('@$"', index):
            prefix_length = 3
            verbatim = True
            delimiter = '"'
        elif text.startswith('@"', index):
            prefix_length = 2
            verbatim = True
            delimiter = '"'
        elif text.startswith('$"', index):
            prefix_length = 2
            delimiter = '"'
        elif text[index] == '"':
            prefix_length = 1
            delimiter = '"'
        elif text[index] == "'":
            prefix_length = 1
            delimiter = "'"
        if not prefix_length:
            index += 1
            continue

        start = index
        index += prefix_length
        while index < len(text):
            if verbatim and text[index] == '"':
                if index + 1 < len(text) and text[index + 1] == '"':
                    index += 2
                    continue
                index += 1
                break
            if not verbatim and text[index] == "\\":
                index += 2
                continue
            if text[index] == delimiter:
                index += 1
                break
            index += 1
        else:
            fail("Unterminated C# string or character literal")

        for position in range(start, min(index, len(masked))):
            if masked[position] not in "\r\n":
                masked[position] = " "
    return "".join(masked)


def matching_brace(text: str, opening: int) -> int:
    depth = 0
    state = "code"
    index = opening
    while index < len(text):
        char = text[index]
        if state == "code":
            if char == '"':
                state = "string"
            elif char == "'":
                state = "char"
            elif char == "{":
                depth += 1
            elif char == "}":
                depth -= 1
                if depth == 0:
                    return index
                if depth < 0:
                    break
        elif char == "\\":
            index += 1
        elif state == "string" and char == '"':
            state = "code"
        elif state == "char" and char == "'":
            state = "code"
        index += 1
    fail("Unbalanced C# braces")


def verify_balanced_syntax(text: str) -> None:
    pairs = {"(": ")", "[": "]", "{": "}"}
    closing = {value: key for key, value in pairs.items()}
    stack: list[str] = []
    state = "code"
    index = 0
    while index < len(text):
        char = text[index]
        if state == "code":
            if char == '"':
                state = "string"
            elif char == "'":
                state = "char"
            elif char in pairs:
                stack.append(char)
            elif char in closing:
                if not stack or stack.pop() != closing[char]:
                    fail("Malformed C# delimiter nesting")
        elif char == "\\":
            index += 1
        elif state == "string" and char == '"':
            state = "code"
        elif state == "char" and char == "'":
            state = "code"
        index += 1
    if state != "code" or stack:
        fail("Malformed C# syntax")


def create_maui_app_body(code: str) -> str:
    match = re.search(r"\bCreateMauiApp\s*\(\s*\)", code)
    if not match:
        fail("CreateMauiApp method is missing")
    opening = code.find("{", match.end())
    if opening < 0:
        fail("CreateMauiApp body is missing")
    closing = matching_brace(code, opening)
    return code[opening + 1 : closing]


def verify_repair(root: Path) -> None:
    expected_files = ["MauiProgram.cs"]
    actual_files = file_set(root)
    if actual_files != expected_files:
        fail(f"Repair fixture file set changed: expected {expected_files}, got {actual_files}")

    source = (root / "MauiProgram.cs").read_text(encoding="utf-8")
    code = strip_comments(source)
    verify_balanced_syntax(code)
    body = create_maui_app_body(mask_literals(code))

    matches = list(REGISTRATION.finditer(body))
    registration_tokens = re.findall(
        r"builder\s*\.\s*Services\s*\.\s*Add(?:Singleton|Transient|Scoped)", body
    )
    if len(matches) != len(registration_tokens):
        fail("A service registration is malformed or incomplete")

    seen: list[str] = []
    for match in matches:
        types = tuple(part.strip() for part in match.group("types").split(","))
        key = types[0]
        if key in EXPECTED_REGISTRATIONS:
            seen.append(key)
            expected_lifetime, expected_types = EXPECTED_REGISTRATIONS[key]
            if match.group("lifetime") != expected_lifetime or types != expected_types:
                fail(f"Conflicting registration for {key}")

    counts = Counter(seen)
    if set(counts) != set(EXPECTED_REGISTRATIONS):
        missing = sorted(set(EXPECTED_REGISTRATIONS) - set(counts))
        fail("Missing required registrations: " + ", ".join(missing))
    duplicates = sorted(key for key, count in counts.items() if count != 1)
    if duplicates:
        fail("Duplicate registrations: " + ", ".join(duplicates))
    if not re.search(r"\breturn\s+builder\s*\.\s*Build\s*\(\s*\)\s*;", body):
        fail("CreateMauiApp must return builder.Build()")


def verify_noop(root: Path) -> None:
    actual = file_set(root)
    expected = sorted(NOOP_HASHES)
    if actual != expected:
        fail(f"Healthy fixture file set changed: expected {expected}, got {actual}")

    for relative, expected_hash in NOOP_HASHES.items():
        digest = hashlib.sha256(
            (root / relative).read_bytes().replace(b"\r\n", b"\n")
        ).hexdigest()
        if digest != expected_hash:
            fail(f"Healthy fixture changed: {relative}")


def expect_failure(action, label: str) -> None:
    try:
        action()
    except (SystemExit, ValueError):
        return
    fail(f"Mutation unexpectedly passed: {label}")


def run_probes() -> None:
    suite = Path(__file__).resolve().parents[1]
    repo = suite.parents[2]
    probe = suite / "validator-probe"
    shutil.rmtree(probe, ignore_errors=True)
    try:
        repair = probe / "repair"
        shutil.copytree(suite / "fixtures" / "repair", repair)
        expect_failure(lambda: verify_repair(repair), "broken fixture")
        subprocess.run(
            [
                "git",
                "apply",
                f"--directory={(probe.relative_to(repo)).as_posix()}",
                str((suite / "references" / "repair-registrations.patch").relative_to(repo)),
            ],
            check=True,
            cwd=repo,
        )
        verify_repair(repair)
        program = repair / "MauiProgram.cs"
        golden = program.read_text(encoding="utf-8")
        required = "builder.Services.AddTransient<InventoryPage>();"

        program.write_text(golden.replace(required, f"// {required}"), encoding="utf-8")
        expect_failure(lambda: verify_repair(repair), "comment-only registration")

        program.write_text(golden[:-2], encoding="utf-8")
        expect_failure(lambda: verify_repair(repair), "malformed braces")

        program.write_text(
            golden.replace(required, f"{required}\n        {required}"), encoding="utf-8"
        )
        expect_failure(lambda: verify_repair(repair), "duplicate registration")

        program.write_text(
            golden.replace(
                required,
                "builder.Services.AddSingleton<InventoryPage>();",
            ),
            encoding="utf-8",
        )
        expect_failure(lambda: verify_repair(repair), "conflicting registration")

        registration_lines = [
            "builder.Services.AddSingleton<IInventoryService, InventoryService>();",
            "builder.Services.AddTransient<InventoryViewModel>();",
            "builder.Services.AddTransient<InventoryPage>();",
            "builder.Services.AddTransient<ItemDetailsViewModel>();",
            "builder.Services.AddTransient<ItemDetailsPage>();",
        ]
        decoys = "\n".join(f'            "{line}",' for line in registration_lines)
        decoy_block = (
            "        var registrationDecoys = new[]\n"
            "        {\n"
            f"{decoys}\n"
            "        };"
        )
        decoy_program = golden
        for line in registration_lines:
            decoy_program = decoy_program.replace(f"        {line}\n", "")
        decoy_program = decoy_program.replace(
            "        return builder.Build();", f"{decoy_block}\n\n        return builder.Build();"
        )
        program.write_text(decoy_program, encoding="utf-8")
        expect_failure(lambda: verify_repair(repair), "string-literal registration decoys")

        program.write_text(golden, encoding="utf-8")
        (repair / "extra.cs").write_text("// unexpected", encoding="utf-8")
        expect_failure(lambda: verify_repair(repair), "extra file")
        verify_noop(suite / "fixtures" / "healthy")
    finally:
        shutil.rmtree(probe, ignore_errors=True)


def main() -> None:
    if len(sys.argv) == 2 and sys.argv[1] == "probe":
        run_probes()
        return
    if len(sys.argv) != 3:
        fail("usage: validate_fixture.py <repair|noop> <fixture-root> | probe")
    mode, root_arg = sys.argv[1:]
    root = Path(root_arg)
    if mode == "repair":
        verify_repair(root)
    elif mode == "noop":
        verify_noop(root)
    else:
        fail(f"unknown mode: {mode}")


if __name__ == "__main__":
    main()
