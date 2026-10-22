import unittest

from amounts import total


class TotalTests(unittest.TestCase):
    def test_total(self):
        self.assertEqual(5, total([2, 3]))

    def test_empty(self):
        self.assertEqual(0, total([]))


if __name__ == "__main__":
    unittest.main()
