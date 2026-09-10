using NUnit.Framework;

namespace Sample.Tests;

[TestFixture]
public class PairwiseTests
{
    private static readonly HashSet<string> Seen = [];

    [Test]
    [Pairwise]
    public void ValuesArePresent(
        [Values("a", "b", "c")] string left,
        [Values("+", "-")] string operation,
        [Values("x", "y")] string right)
    {
        lock (Seen)
        {
            Seen.Add($"{left} {operation} {right}");
        }

        Assert.That(left, Is.Not.Empty);
        Assert.That(operation, Is.Not.Empty);
        Assert.That(right, Is.Not.Empty);
    }

    [OneTimeTearDown]
    public void VerifyPairwiseCoverage()
    {
        string[] expected =
        [
            "a - x",
            "a + y",
            "b - y",
            "b + x",
            "c - x",
            "c + y",
        ];

        Assert.That(Seen, Is.EquivalentTo(expected));
    }
}
