from pathlib import Path
import sys
import xml.etree.ElementTree as ET


def local_name(name: str) -> str:
    return name.rsplit("}", 1)[-1]


def attribute(element: ET.Element, name: str) -> str | None:
    return next(
        (value for key, value in element.attrib.items() if local_name(key) == name),
        None,
    )


def children(element: ET.Element, name: str) -> list[ET.Element]:
    return [child for child in list(element) if local_name(child.tag) == name]


def assert_file_set(root: Path, expected: set[str]) -> None:
    def ignored(path: Path) -> bool:
        relative = path.relative_to(root)
        if ".eval" in relative.parts:
            return True
        current = path.parent
        while current != root.parent:
            if (current / "SKILL.md").is_file():
                return True
            if current == root:
                break
            current = current.parent
        return False

    actual = {
        path.relative_to(root).as_posix()
        for path in root.rglob("*")
        if path.is_file() and not ignored(path)
    }
    if actual != expected:
        raise AssertionError(f"unexpected file set: {sorted(actual)}")


target = Path(sys.argv[1]).resolve()
root_dir = target.parent
assert_file_set(root_dir, {"ChatPage.xaml"})
try:
    page = ET.parse(target).getroot()
except ET.ParseError as error:
    raise AssertionError(f"malformed XAML: {error}") from error

if local_name(page.tag) != "ContentPage" or attribute(page, "SafeAreaEdges") != "None":
    raise AssertionError("ContentPage must remain edge-to-edge")
root_grids = children(page, "Grid")
if len(root_grids) != 1:
    raise AssertionError("ContentPage must contain exactly one root Grid")
chat_grid = root_grids[0]
if attribute(chat_grid, "RowDefinitions") != "*,Auto":
    raise AssertionError("root chat Grid must keep the message/composer rows")
regions = [part.strip() for part in (attribute(chat_grid, "SafeAreaEdges") or "").split(",")]
if regions != ["Container", "Container", "Container", "SoftInput"]:
    raise AssertionError("root chat Grid must reserve only its bottom edge for SoftInput")

scroll_views = children(chat_grid, "ScrollView")
composer_grids = [
    child
    for child in children(chat_grid, "Grid")
    if attribute(child, "Grid.Row") == "1"
]
if len(scroll_views) != 1 or attribute(scroll_views[0], "Grid.Row") != "0":
    raise AssertionError("message ScrollView must occupy row 0")
if len(composer_grids) != 1:
    raise AssertionError("exactly one composer Grid must occupy row 1")
entries = [element for element in composer_grids[0].iter() if local_name(element.tag) == "Entry"]
if len(entries) != 1 or attribute(entries[0], "Placeholder") != "Message":
    raise AssertionError("composer must contain exactly one Message Entry")
if any(
    "SoftInput" in (attribute(element, "SafeAreaEdges") or "")
    for element in page.iter()
    if local_name(element.tag) == "ScrollView"
):
    raise AssertionError("ScrollView does not honor the SoftInput region")
if any(
    local_name(key) in {"WindowSoftInputModeAdjust", "IgnoreSafeArea"}
    for element in page.iter()
    for key in element.attrib
):
    raise AssertionError("legacy platform-specific workaround remains")
