from pathlib import Path
import hashlib
import shutil
import subprocess
import sys
import tempfile


BASE = Path(__file__).resolve().parents[1]
VALIDATOR = BASE / "fixtures/repair/validate.py"
BROKEN = BASE / "fixtures/repair/SettingsPage.xaml"
GOLDEN = BASE / "references/repair/SettingsPage.xaml"
EXPECTED_HASH = "ac110838d633877de4bd450ace9a624ad27c008bb0884b9de5b0ee4c54b29f4a"


def authenticated_run(work: Path) -> bool:
    validator = work / ".eval/validate.py"
    digest = hashlib.sha256(validator.read_bytes().replace(b"\r\n", b"\n")).hexdigest()
    if digest != EXPECTED_HASH:
        return False
    return subprocess.run(
        [sys.executable, str(validator), "SettingsPage.xaml"],
        cwd=work,
        capture_output=True,
    ).returncode == 0


def expect(name: str, source: str, passes: bool, extra: bool = False, tamper: bool = False):
    with tempfile.TemporaryDirectory(dir=BASE / "references") as directory:
        root = Path(directory)
        (root / ".eval").mkdir()
        shutil.copy2(VALIDATOR, root / ".eval/validate.py")
        (root / "SettingsPage.xaml").write_text(source, encoding="utf-8")
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
comments_only = golden.replace('\n    x:DataType="vm:SettingsViewModel"', "").replace(
    ' x:DataType="model:Option"', ""
).replace(
    "<VerticalStackLayout>",
    '<!-- x:DataType="vm:SettingsViewModel"; DataTemplate x:DataType="model:Option" -->\n    <VerticalStackLayout>',
)
duplicate_template = golden.replace(
    "</CollectionView.ItemTemplate>",
    """    <DataTemplate x:DataType="model:Option">
        <Label Text="{Binding DisplayName}" />
    </DataTemplate>
</CollectionView.ItemTemplate>""",
)
wrong_element = golden.replace('<Label Text="{Binding DisplayName}" />', '<Entry Text="{Binding DisplayName}" />')

expect("broken fixture", BROKEN.read_text(encoding="utf-8"), False)
expect("golden fix", golden, True)
expect("malformed XML", golden.replace("</ContentPage>", ""), False)
expect("required attributes only in comments", comments_only, False)
expect("duplicate template", duplicate_template, False)
expect("wrong template element", wrong_element, False)
expect("extra file", golden, False, extra=True)
expect("modified validator", golden, False, tamper=True)
