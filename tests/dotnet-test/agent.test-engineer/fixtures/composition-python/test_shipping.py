import unittest

from shipping import quote


class ShippingTests(unittest.TestCase):
    def test_standard(self):
        self.assertEqual(5, quote(25))

    def test_threshold(self):
        self.assertEqual(0, quote(100))

    def test_negative(self):
        with self.assertRaises(ValueError):
            quote(-1)
