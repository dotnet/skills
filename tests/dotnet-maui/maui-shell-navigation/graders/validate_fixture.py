from __future__ import annotations

import hashlib
import re
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET
from collections import Counter
from pathlib import Path


NOOP_HASHES = {
    "AppShell.xaml": "9d48f7193fea36c02ce963a5778653c20c680776fd1d4b712f8cc2cc27970b0d",
    "AppShell.xaml.cs": "d4d51fcb702d8f68389022e507c2b28107f4fe25e852dee7fbccfe2e4876facc",
}
EXPECTED_CONTENT = {
    "Home": ("home", "{DataTemplate views:HomePage}"),
    "Orders": ("orders", "{DataTemplate views:OrdersPage}"),
}
REGISTER_ROUTE = re.compile(
    r"Routing\s*\.\s*RegisterRoute\s*\(\s*"
    r'"(?P<route>[^"]+)"\s*,\s*'
    r"typeof\s*\(\s*(?P<page>[A-Za-z_]\w*)\s*\)\s*\)\s*;"
)


def fail(message: str) -> None:
    raise SystemExit(message)


def local_name(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


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


def constructor_body(code: str) -> str:
    match = re.search(r"\bAppShell\s*\(\s*\)", code)
    if not match:
        fail("AppShell constructor is missing")
    opening = code.find("{", match.end())
    if opening < 0:
        fail("AppShell constructor body is missing")
    closing = matching_brace(code, opening)
    return code[opening + 1 : closing]


def verify_xaml(path: Path) -> None:
    try:
        shell = ET.fromstring(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, ET.ParseError) as exc:
        fail(f"AppShell.xaml is not valid XML: {exc}")
    if local_name(shell.tag) != "Shell":
        fail("The document root must be Shell")

    all_elements = list(shell.iter())
    eager_pages = [
        local_name(element.tag)
        for element in all_elements
        if local_name(element.tag) in {"HomePage", "OrdersPage"}
    ]
    if eager_pages:
        fail("Shell pages must not be eagerly instantiated")

    flyouts = [
        child for child in list(shell) if local_name(child.tag) == "FlyoutItem"
    ]
    if len(flyouts) != len(EXPECTED_CONTENT):
        fail("Expected exactly the Home and Orders FlyoutItem elements")

    found: dict[str, tuple[str, str]] = {}
    routes: list[str] = []
    for flyout in flyouts:
        title = flyout.attrib.get("Title")
        contents = [
            child for child in list(flyout) if local_name(child.tag) == "ShellContent"
        ]
        if title not in EXPECTED_CONTENT or len(contents) != 1 or len(list(flyout)) != 1:
            fail("Each expected FlyoutItem must contain exactly one ShellContent")
        content = contents[0]
        route = content.attrib.get("Route")
        template = content.attrib.get("ContentTemplate")
        if list(content):
            fail("ShellContent must use a lazy ContentTemplate, not child page content")
        found[title] = (route or "", template or "")
        routes.append(route or "")

    if found != EXPECTED_CONTENT:
        fail(f"Shell hierarchy differs from expected routes/templates: {found}")
    if "" in routes or len(routes) != len(set(routes)):
        fail("ShellContent routes must be present and unique")


def verify_code(path: Path) -> None:
    source = path.read_text(encoding="utf-8")
    code = strip_comments(source)
    verify_balanced_syntax(code)
    body = constructor_body(code)
    if not re.search(r"\bInitializeComponent\s*\(\s*\)\s*;", body):
        fail("AppShell constructor must call InitializeComponent()")

    matches = list(REGISTER_ROUTE.finditer(body))
    route_tokens = re.findall(r"Routing\s*\.\s*RegisterRoute", body)
    if len(matches) != len(route_tokens):
        fail("A route registration is malformed or incomplete")
    registrations = [(m.group("route"), m.group("page")) for m in matches]
    if registrations != [("orderdetails", "OrderDetailsPage")]:
        fail(f"Expected one orderdetails registration, got {registrations}")
    duplicates = [route for route, count in Counter(r for r, _ in registrations).items() if count > 1]
    if duplicates:
        fail("Duplicate registered routes: " + ", ".join(duplicates))


def verify_repair(root: Path) -> None:
    expected_files = ["AppShell.xaml", "AppShell.xaml.cs"]
    actual_files = file_set(root)
    if actual_files != expected_files:
        fail(f"Repair fixture file set changed: expected {expected_files}, got {actual_files}")
    verify_xaml(root / "AppShell.xaml")
    verify_code(root / "AppShell.xaml.cs")


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
    except (SystemExit, ET.ParseError, ValueError):
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
                str((suite / "references" / "repair-shell.patch").relative_to(repo)),
            ],
            check=True,
            cwd=repo,
        )
        verify_repair(repair)
        xaml_path = repair / "AppShell.xaml"
        code_path = repair / "AppShell.xaml.cs"
        golden_xaml = xaml_path.read_text(encoding="utf-8")
        golden_code = code_path.read_text(encoding="utf-8")
        registration = 'Routing.RegisterRoute("orderdetails", typeof(OrderDetailsPage));'

        code_path.write_text(
            golden_code.replace(registration, f"// {registration}"), encoding="utf-8"
        )
        expect_failure(lambda: verify_repair(repair), "comment-only registration")

        code_path.write_text(golden_code[:-2], encoding="utf-8")
        expect_failure(lambda: verify_repair(repair), "malformed C#")

        xaml_path.write_text(golden_xaml + "<", encoding="utf-8")
        code_path.write_text(golden_code, encoding="utf-8")
        expect_failure(lambda: verify_repair(repair), "malformed XML")

        duplicate_route = golden_xaml.replace('Route="orders"', 'Route="home"')
        xaml_path.write_text(duplicate_route, encoding="utf-8")
        expect_failure(lambda: verify_repair(repair), "duplicate route")

        eager = golden_xaml.replace(
            'ContentTemplate="{DataTemplate views:HomePage}" />',
            'ContentTemplate="{DataTemplate views:HomePage}"><views:HomePage /></ShellContent>',
        )
        xaml_path.write_text(eager, encoding="utf-8")
        expect_failure(lambda: verify_repair(repair), "eager page")

        xaml_path.write_text(golden_xaml, encoding="utf-8")
        (repair / "extra.txt").write_text("unexpected", encoding="utf-8")
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
