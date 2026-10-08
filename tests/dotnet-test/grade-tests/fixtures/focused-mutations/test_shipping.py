from pathlib import Path
import unittest

from shipping import parse_quantity, quote


def setUpModule():
    Path(".test-executed").write_text("Tests were executed.", encoding="utf-8")


class ShippingTests(unittest.TestCase):
    def test_standard_quote_cost_is_positive(self):
        ordinary_subtotal = 50

        result = quote(ordinary_subtotal)

        self.assertGreater(result.cost, 0)

    def test_standard_quote_calculates_cost(self):
        ordinary_subtotal = 50

        result = quote(ordinary_subtotal)

        self.assertGreater(result.cost, 0)

    def test_standard_quote_cost_is_ten(self):
        ordinary_subtotal = 50
        expected_standard_cost = 10

        result = quote(ordinary_subtotal)

        self.assertEqual(expected_standard_cost, result.cost)

    def test_free_shipping_starts_at_threshold(self):
        free_shipping_threshold = 100
        expected_cost_at_threshold = 0

        result = quote(free_shipping_threshold)

        self.assertEqual(expected_cost_at_threshold, result.cost)

    def test_empty_quantity_raises_value_error(self):
        empty_quantity = ""

        with self.assertRaises(ValueError):
            parse_quantity(empty_quantity)
