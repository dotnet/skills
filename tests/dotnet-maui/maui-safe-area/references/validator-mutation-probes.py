from pathlib import Path
import hashlib
import shutil
import subprocess
import sys
import tempfile


BASE = Path(__file__).resolve().parents[1]
VALIDATOR = BASE / "fixtures/repair/validate.py"
BROKEN = BASE / "fixtures/repair/ChatPage.xaml"
GOLDEN = BASE / "references/repair/ChatPage.xaml"
EXPECTED_HASH = "a46687177a2eeb3600b707c19acbe9b25278ffb71b731f9f39302a4864b13799"


def authenticated_run(work: Path) -> bool:
    validator = work / ".eval/validate.py"
    digest = hashlib.sha256(validator.read_bytes().replace(b"\r\n", b"\n")).hexdigest()
    if digest != EXPECTED_HASH:
        return False
    return subprocess.run(
        [sys.executable, str(validator), "ChatPage.xaml"],
        cwd=work,
        capture_output=True,
    ).returncode == 0


def expect(name: str, source: str, passes: bool, extra: bool = False, tamper: bool = False):
    with tempfile.TemporaryDirectory(dir=BASE / "references") as directory:
        root = Path(directory)
        (root / ".eval").mkdir()
        shutil.copy2(VALIDATOR, root / ".eval/validate.py")
        (root / "ChatPage.xaml").write_text(source, encoding="utf-8")
        if extra:
            (root / "Unexpected.xaml").write_text("<ContentPage />", encoding="utf-8")
        if tamper:
            (root / ".eval/validate.py").write_text(
                (root / ".eval/validate.py").read_text(encoding="utf-8") + "\n# tampered\n",
                encoding="utf-8",
            )
        actual = authenticated_run(root)
        if actual != passes:
            raise AssertionError(f"{name}: expected {passes}, got {actual}")
        print(f"{name}: {'pass' if actual else 'reject'}")


golden = GOLDEN.read_text(encoding="utf-8")
comments_only = golden.replace(
    '\n          SafeAreaEdges="Container, Container, Container, SoftInput"',
    "",
).replace(
    '<Grid RowDefinitions="*,Auto">',
    '<!-- SafeAreaEdges="Container, Container, Container, SoftInput" -->\n    <Grid RowDefinitions="*,Auto">',
)
duplicate_root = golden.replace(
    "</ContentPage>",
    '    <Grid RowDefinitions="*,Auto" SafeAreaEdges="Container, Container, Container, SoftInput" />\n</ContentPage>',
)
wrong_composer = golden.replace('<Entry Placeholder="Message" />', '<Label Text="Message" />')

expect("broken fixture", BROKEN.read_text(encoding="utf-8"), False)
expect("golden fix", golden, True)
expect("malformed XML", golden.replace("</ContentPage>", ""), False)
expect("required attribute only in comment", comments_only, False)
expect("duplicate root Grid", duplicate_root, False)
expect("wrong composer element", wrong_composer, False)
expect("extra file", golden, False, extra=True)
expect("modified validator", golden, False, tamper=True)
