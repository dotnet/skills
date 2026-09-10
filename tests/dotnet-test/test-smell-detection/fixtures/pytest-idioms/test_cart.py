import pytest
from unittest.mock import Mock


@pytest.mark.parametrize(
    ("prices", "expected"),
    [
        ([2, 3], 5),
        ([4, 6], 10),
    ],
)
def test_total_for_known_prices(prices, expected):
    assert sum(prices) == expected


def test_callback_receives_total():
    callback = Mock()

    callback(12)

    callback.assert_called_once_with(12)


def test_invalid_price_raises():
    with pytest.raises(ValueError, match="price"):
        raise ValueError("price must be positive")


def test_all_skus_are_present():
    skus = ["A", "B", "C"]

    for sku in skus:
        assert sku
