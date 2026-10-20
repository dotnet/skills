from pathlib import Path
import hashlib
import shutil
import subprocess
import sys
import tempfile


BASE = Path(__file__).resolve().parents[1]
VALIDATOR = BASE / "fixtures/repair/validate.py"
BROKEN_SERVICE = BASE / "fixtures/repair/ThemeService.cs"
BROKEN_PAGE = BASE / "fixtures/repair/MainPage.xaml"
GOLDEN_SERVICE = BASE / "references/repair/ThemeService.cs"
GOLDEN_PAGE = BASE / "references/repair/MainPage.xaml"
EXPECTED_HASH = "e840a6c74f043b0b667c0236b8f4879de6ca2256acfdc7827ba7671f92a188cb"


def authenticated_run(work: Path) -> bool:
    validator = work / ".eval/validate.py"
    digest = hashlib.sha256(validator.read_bytes().replace(b"\r\n", b"\n")).hexdigest()
    if digest != EXPECTED_HASH:
        return False
    return subprocess.run(
        [sys.executable, str(validator), "."],
        cwd=work,
        capture_output=True,
    ).returncode == 0


def expect(
    name: str,
    service: str,
    page: str,
    passes: bool,
    extra: bool = False,
    tamper: bool = False,
):
    with tempfile.TemporaryDirectory(dir=BASE / "references") as directory:
        root = Path(directory)
        (root / ".eval").mkdir()
        shutil.copy2(VALIDATOR, root / ".eval/validate.py")
        (root / "ThemeService.cs").write_text(service, encoding="utf-8")
        (root / "MainPage.xaml").write_text(page, encoding="utf-8")
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


service = GOLDEN_SERVICE.read_text(encoding="utf-8")
page = GOLDEN_PAGE.read_text(encoding="utf-8")
commented_remove = service.replace(
    "            merged.Remove(_currentTheme);",
    "            // merged.Remove(_currentTheme);",
)
commented_xaml = page.replace(
    'BackgroundColor="{DynamicResource PageBackgroundColor}"',
    'BackgroundColor="White"',
).replace(
    "<ContentPage",
    '<!-- BackgroundColor="{DynamicResource PageBackgroundColor}" -->\n<ContentPage',
    1,
)
duplicate_label = page.replace(
    "</ContentPage>",
    '    <Label Text="Welcome" TextColor="{DynamicResource PrimaryTextColor}" />\n</ContentPage>',
)

expect(
    "broken fixture",
    BROKEN_SERVICE.read_text(encoding="utf-8"),
    BROKEN_PAGE.read_text(encoding="utf-8"),
    False,
)
expect("golden fix", service, page, True)
expect("malformed C# braces", service[:-2], page, False)
expect("malformed XML", service, page.replace("</ContentPage>", ""), False)
expect("required C# token only in comment", commented_remove, page, False)
expect("required XAML token only in comment", service, commented_xaml, False)
expect("duplicate Label", service, duplicate_label, False)
expect("extra file", service, page, False, extra=True)
expect("modified validator", service, page, False, tamper=True)
