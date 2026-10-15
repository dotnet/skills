import unittest
from limit import allowed


class LimitTests(unittest.TestCase):
    def test_exact_threshold_is_allowed(self):
        self.assertTrue(allowed(10))
