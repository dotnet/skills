import unittest

from boundary import shipping_cost


class BoundaryTests(unittest.TestCase):
    def test_below_threshold(self):
        self.assertEqual(10, shipping_cost(99))

    def test_exact_threshold(self):
        self.assertEqual(0, shipping_cost(100))

    def test_above_threshold(self):
        self.assertEqual(0, shipping_cost(101))


if __name__ == "__main__":
    unittest.main()
