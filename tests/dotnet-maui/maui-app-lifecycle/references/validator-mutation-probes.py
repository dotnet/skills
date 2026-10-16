from pathlib import Path
import hashlib
import shutil
import subprocess
import sys
import tempfile


BASE = Path(__file__).resolve().parents[1]
VALIDATOR = BASE / "fixtures/repair/validate.py"
BROKEN = BASE / "fixtures/repair/App.cs"
GOLDEN = BASE / "references/repair/App.cs"
EXPECTED_HASH = "bf8569d81fc9732cae9bb8827e37f7d5005406cc7d9b57c42c3508061efeddb9"


def authenticated_run(work: Path) -> bool:
    validator = work / ".eval/validate.py"
    digest = hashlib.sha256(validator.read_bytes().replace(b"\r\n", b"\n")).hexdigest()
    if digest != EXPECTED_HASH:
        return False
    return subprocess.run(
        [sys.executable, str(validator), "App.cs"],
        cwd=work,
        capture_output=True,
    ).returncode == 0


def workspace(source: str):
    temporary = tempfile.TemporaryDirectory(dir=BASE / "references")
    root = Path(temporary.name)
    (root / ".eval").mkdir()
    shutil.copy2(VALIDATOR, root / ".eval/validate.py")
    (root / "App.cs").write_text(source, encoding="utf-8")
    return temporary, root


def expect(name: str, source: str, passes: bool, extra: bool = False, tamper: bool = False):
    temporary, root = workspace(source)
    try:
        if extra:
            (root / "Unexpected.cs").write_text("class Unexpected {}", encoding="utf-8")
        if tamper:
            (root / ".eval/validate.py").write_text(
                (root / ".eval/validate.py").read_text(encoding="utf-8") + "\n# tampered\n",
                encoding="utf-8",
            )
        actual = authenticated_run(root)
        if actual != passes:
            raise AssertionError(f"{name}: expected {passes}, got {actual}")
        print(f"{name}: {'pass' if actual else 'reject'}")
    finally:
        temporary.cleanup()


golden = GOLDEN.read_text(encoding="utf-8")
comments_only = golden.replace(
    """    protected override void OnStopped()
    {
        base.OnStopped();
        Preferences.Set("draft_text", _viewModel.DraftText);
    }
""",
    """    /*
    protected override void OnStopped()
    {
        base.OnStopped();
        Preferences.Set("draft_text", _viewModel.DraftText);
    }
    */
""",
)
duplicate = golden.replace(
    "\n}\n",
    """
    protected override void OnStopped()
    {
        base.OnStopped();
        Preferences.Set("draft_text", _viewModel.DraftText);
    }
}
""",
    1,
)

expect("broken fixture", BROKEN.read_text(encoding="utf-8"), False)
expect("golden fix", golden, True)
expect("malformed braces", golden[:-2], False)
expect("required tokens only in comments", comments_only, False)
expect("duplicate method", duplicate, False)
expect("extra file", golden, False, extra=True)
expect("modified validator", golden, False, tamper=True)
