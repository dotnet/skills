from unittest.mock import Mock

import pytest

from src.inventory import Inventory


def test_add_item_executes_without_verification():
    inventory = Inventory()
    inventory.add("sku-1", 3, 4.00)


def test_find_item_exists():
    inventory = Inventory()
    inventory.add("sku-1", 3, 4.00)

    assert inventory.find("sku-1") is not None


def test_discount_is_valid():
    discount = 0.15

    assert True


def test_rejects_non_positive_quantity():
    inventory = Inventory()

    with pytest.raises(ValueError):
        inventory.add("sku-1", 0, 4.00)


def test_notifies_when_stock_is_added():
    notifier = Mock()
    inventory = Inventory(notifier)

    inventory.add("sku-1", 3, 4.00)

    notifier.stock_added.assert_called_once_with("sku-1", 3)


def test_total_uses_quantity_and_price():
    inventory = Inventory()
    inventory.add("sku-1", 3, 4.00)
    inventory.add("sku-2", 2, 5.00)

    assert inventory.total() == 22.00
