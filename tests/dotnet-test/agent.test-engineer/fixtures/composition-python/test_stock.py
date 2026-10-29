import unittest

from stock import Stock


class StockTests(unittest.TestCase):
    def test_reserve(self):
        stock = Stock(4)
        self.assertIsNotNone(stock.reserve(2))

    def test_shortage(self):
        stock = Stock(4)
        self.assertIsNotNone(stock.reserve(5))

    def test_invalid(self):
        with self.assertRaises(ValueError):
            Stock(-1)
        for count in (0, -1):
            with self.assertRaises(ValueError):
                Stock(4).reserve(count)
