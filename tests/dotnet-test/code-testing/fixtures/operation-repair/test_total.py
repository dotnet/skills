import unittest
from amount import total


class TotalTests(unittest.TestCase):
    def test_total(self):
        self.assertEqual(5, total([2, 3]))
