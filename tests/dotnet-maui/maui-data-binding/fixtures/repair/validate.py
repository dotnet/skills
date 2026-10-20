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
assert_file_set(root_dir, {"SettingsPage.xaml"})
try:
    page = ET.parse(target).getroot()
except ET.ParseError as error:
    raise AssertionError(f"malformed XAML: {error}") from error

if local_name(page.tag) != "ContentPage":
    raise AssertionError("root element must be ContentPage")
if attribute(page, "DataType") != "vm:SettingsViewModel":
    raise AssertionError("ContentPage must declare the SettingsViewModel binding scope")

templates = [element for element in page.iter() if local_name(element.tag) == "DataTemplate"]
if len(templates) != 1 or attribute(templates[0], "DataType") != "model:Option":
    raise AssertionError("exactly one Option-typed DataTemplate is required")
if any(attribute(element, "DataType") == "x:Object" for element in page.iter()):
    raise AssertionError("x:Object disables compiled binding checks")

collections = [element for element in page.iter() if local_name(element.tag) == "CollectionView"]
if len(collections) != 1 or attribute(collections[0], "ItemsSource") != "{Binding Options}":
    raise AssertionError("exactly one Options CollectionView is required")
template_labels = [
    element for element in templates[0].iter() if local_name(element.tag) == "Label"
]
if len(template_labels) != 1 or attribute(template_labels[0], "Text") != "{Binding DisplayName}":
    raise AssertionError("the item template must contain one DisplayName Label")
all_labels = [element for element in page.iter() if local_name(element.tag) == "Label"]
title_labels = [element for element in all_labels if attribute(element, "Text") == "{Binding Title}"]
if len(title_labels) != 1 or len(all_labels) != 2:
    raise AssertionError("the page must contain only the Title and DisplayName Labels")
