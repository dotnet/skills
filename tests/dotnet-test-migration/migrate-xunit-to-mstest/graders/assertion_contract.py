"""Check all supplied assertion-library import forms after framework migration."""

from pathlib import Path
import re
import xml.etree.ElementTree as ET


project = ET.parse("TestProject.csproj").getroot()
packages = [
    item for item in project.iter("PackageReference")
    if item.get("Include") == "AwesomeAssertions"
]
assert len(packages) == 1 and packages[0].get("Version") == "9.6.0"
assert any(item.get("Include") == "AwesomeAssertions" for item in project.iter("Using"))
assert not any(
    item.get("Include", "").lower().startswith(("xunit", "fluentassertions"))
    for item in project.iter("PackageReference")
)
local = Path("LocalTests.cs").read_text(encoding="utf-8")
global_import = Path("GlobalUsings.cs").read_text(encoding="utf-8")
assert re.search(r"(?m)^\s*using\s+AwesomeAssertions\s*;", local)
assert re.search(r"(?m)^\s*global\s+using\s+AwesomeAssertions\s*;", global_import)
for file, method, expression in (
    ("LocalTests.cs", "PreservesLocalAssertions", "(2+2).Should().Be(4)"),
    ("GlobalTests.cs", "PreservesGlobalAssertions", "new[]{2,4}.Should().Equal(2,4)"),
):
    text = Path(file).read_text(encoding="utf-8")
    code = re.sub(r"//[^\n]*|/\*.*?\*/", "", text, flags=re.S)
    code = re.sub(r"\s+", "", code)
    signature = "void" + method + "()"
    assert (
        signature + "=>" + expression + ";" in code
        or signature + "{" + expression + ";}" in code
    ), file
    assert not re.search(r"\b(?:Xunit|FluentAssertions)\b", text), file
print("ASSERTION_CONTRACT:package and all three imports preserved")
