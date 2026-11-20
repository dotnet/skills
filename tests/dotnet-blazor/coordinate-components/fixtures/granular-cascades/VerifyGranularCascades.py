import sys
from pathlib import Path


expected_consumers = {
    "ThemePreview": "ThemeState",
    "LocalePreview": "LocaleState",
    "CartSummary": "CartState",
    "ConnectionBadge": "ConnectionState",
}

for component, expected_state in expected_consumers.items():
    files = list(Path(".").glob(f"**/{component}.razor"))
    files += list(Path(".").glob(f"**/{component}.razor.cs"))
    text = "\n".join(path.read_text() for path in files)
    if not files or expected_state not in text or "AppState" in text:
        sys.exit(1)

shell_files = list(Path(".").glob("**/SettingsShell.razor"))
shell_files += list(Path(".").glob("**/SettingsShell.razor.cs"))
shell_text = "\n".join(path.read_text() for path in shell_files)
if not shell_files or "AppState" in shell_text:
    sys.exit(1)

program_text = "\n".join(path.read_text() for path in Path(".").glob("**/Program.cs"))
if not all(state in program_text for state in expected_consumers.values()):
    sys.exit(1)
