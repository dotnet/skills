import unittest
from limit import allowed


class ExistingTests(unittest.TestCase):
    def test_large_value_is_allowed(self):
        self.assertTrue(allowed(20))
