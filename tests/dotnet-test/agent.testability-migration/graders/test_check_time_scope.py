import unittest

from check_time_scope import export_body


TARGET = """
public class SubscriptionManager
{
    public void ExportSubscription(Subscription sub)
    {
        File.WriteAllText(sub.UserId, "value");
    }
}
"""


class TimeScopeTests(unittest.TestCase):
    def test_unique_target_body_is_extracted(self):
        self.assertIn("WriteAllText", export_body(TARGET))

    def test_different_overload_does_not_mask_target(self):
        source = TARGET.replace(
            "public void ExportSubscription(Subscription sub)",
            "public void ExportSubscription(string sub) { }\n"
            "    public void ExportSubscription(Subscription sub)",
        )
        self.assertEqual(export_body(source), export_body(TARGET))

    def test_duplicate_exact_signature_is_rejected(self):
        duplicate = TARGET.replace(
            "\n}",
            "\n    public void ExportSubscription(Subscription sub) { }\n}",
        )
        with self.assertRaisesRegex(ValueError, "exactly one"):
            export_body(duplicate)

    def test_changed_target_is_detected_despite_overload(self):
        source = TARGET.replace(
            "public void ExportSubscription(Subscription sub)",
            "public void ExportSubscription(string sub) { }\n"
            "    public void ExportSubscription(Subscription sub)",
        ).replace('File.WriteAllText(sub.UserId, "value");', "Console.WriteLine(sub.UserId);")
        self.assertNotEqual(export_body(source), export_body(TARGET))


if __name__ == "__main__":
    unittest.main()
