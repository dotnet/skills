import unittest

from shipping import quote


class ShippingTests(unittest.TestCase):
    def test_domestic_partitions(self):
        for subtotal, expected in ((0, 5), (25, 5), (99, 5), (100, 0), (101, 0)):
            with self.subTest(subtotal=subtotal):
                self.assertEqual(expected, quote(subtotal))

    def test_remote_partitions(self):
        for subtotal, expected in ((0, 13), (25, 13), (99, 13), (100, 8), (101, 8)):
            with self.subTest(subtotal=subtotal):
                self.assertEqual(expected, quote(subtotal, remote=True))

    def test_negative(self):
        for remote in (False, True):
            with self.subTest(remote=remote), self.assertRaises(ValueError):
                quote(-1, remote)
