from src.orders.pricing import calculate_total


def test_calculate_total_adds_tax():
    assert calculate_total(1000, 200) == 1200
