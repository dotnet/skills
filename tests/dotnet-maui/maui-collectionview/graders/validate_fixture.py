from __future__ import annotations

import hashlib
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


NOOP_HASHES = {
    "CatalogPage.xaml": "5964b26df48f601bfc2d531f5d7dfbe3989bbfcc647a12cec0a94d683b8736fb",
    "CatalogViewModel.cs": "dfb6301bee97393a4d097d1ff3463876608548b282141a1d859e08990b5b9979",
    "Product.cs": "1fb08e6c0b32158ae4e73c052908c679b3c90375b3323b5efb7ef9b1edf7d626",
}
XAML_NS = "http://schemas.microsoft.com/winfx/2009/xaml"
MAUI_NS = "http://schemas.microsoft.com/dotnet/2021/maui"
GRID_COLUMN = "Grid.Column"
X_DATA_TYPE = f"{{{XAML_NS}}}DataType"


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


def verify_repair(root: Path) -> None:
    expected_files = [
        "InventoryItem.cs",
        "InventoryPage.xaml",
        "InventoryViewModel.cs",
    ]
    actual_files = file_set(root)
    if actual_files != expected_files:
        fail(f"Repair fixture file set changed: expected {expected_files}, got {actual_files}")

    page_path = root / "InventoryPage.xaml"
    try:
        page = ET.fromstring(page_path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, ET.ParseError) as exc:
        fail(f"InventoryPage.xaml is not valid XML: {exc}")

    if local_name(page.tag) != "ContentPage":
        fail("The document root must be ContentPage")
    if page.attrib.get(X_DATA_TYPE) != "vm:InventoryViewModel":
        fail("ContentPage must declare x:DataType=\"vm:InventoryViewModel\"")

    all_elements = list(page.iter())
    if any(local_name(element.tag) == "ViewCell" for element in all_elements):
        fail("CollectionView templates must not contain ViewCell")

    collections = [
        element for element in all_elements if local_name(element.tag) == "CollectionView"
    ]
    if len(collections) != 1:
        fail("Expected exactly one CollectionView")
    collection = collections[0]
    if collection.attrib.get("ItemsSource") != "{Binding Items}":
        fail("CollectionView must bind ItemsSource to Items")

    item_template_nodes = [
        child
        for child in list(collection)
        if local_name(child.tag) == "CollectionView.ItemTemplate"
    ]
    if len(item_template_nodes) != 1:
        fail("Expected exactly one CollectionView.ItemTemplate")

    data_templates = [
        element for element in all_elements if local_name(element.tag) == "DataTemplate"
    ]
    if len(data_templates) != 1:
        fail("Expected exactly one DataTemplate")
    template = data_templates[0]
    if list(item_template_nodes[0]) != [template]:
        fail("ItemTemplate must contain only its DataTemplate")
    if template.attrib.get(X_DATA_TYPE) != "models:InventoryItem":
        fail("DataTemplate must declare x:DataType=\"models:InventoryItem\"")

    template_children = list(template)
    if len(template_children) != 1 or local_name(template_children[0].tag) != "Grid":
        fail("DataTemplate must have exactly one Grid root")
    grid = template_children[0]
    if grid.attrib.get("Padding") != "12":
        fail("Grid Padding must remain 12")
    if grid.attrib.get("ColumnDefinitions") != "*,Auto":
        fail("Grid column structure must remain *,Auto")

    labels = [child for child in list(grid) if local_name(child.tag) == "Label"]
    if len(labels) != 2 or len(list(grid)) != 2:
        fail("Grid must contain exactly the two expected Label elements")
    if labels[0].attrib.get("Text") != "{Binding Name}":
        fail("First label must bind Name")
    if labels[1].attrib.get("Text") != "{Binding Quantity}":
        fail("Second label must bind Quantity")
    if labels[1].attrib.get(GRID_COLUMN) != "1":
        fail("Quantity label must remain in Grid column 1")


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
                str((suite / "references" / "repair-inventory.patch").relative_to(repo)),
            ],
            check=True,
            cwd=repo,
        )
        verify_repair(repair)
        golden = (repair / "InventoryPage.xaml").read_text(encoding="utf-8")

        comment_only = (
            '<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui" '
            f'xmlns:x="{XAML_NS}"><!-- {golden} --><CollectionView /></ContentPage>'
        )
        (repair / "InventoryPage.xaml").write_text(comment_only, encoding="utf-8")
        expect_failure(lambda: verify_repair(repair), "comment-only required tokens")

        (repair / "InventoryPage.xaml").write_text(golden + "<", encoding="utf-8")
        expect_failure(lambda: verify_repair(repair), "malformed XML")

        duplicate = golden.replace(
            "</CollectionView.ItemTemplate>",
            '<DataTemplate x:DataType="models:InventoryItem"><Grid /></DataTemplate>'
            "</CollectionView.ItemTemplate>",
        )
        (repair / "InventoryPage.xaml").write_text(duplicate, encoding="utf-8")
        expect_failure(lambda: verify_repair(repair), "duplicate template")

        (repair / "InventoryPage.xaml").write_text(golden, encoding="utf-8")
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
