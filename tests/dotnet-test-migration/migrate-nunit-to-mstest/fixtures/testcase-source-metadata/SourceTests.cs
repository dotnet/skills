using System.Collections;
using NUnit.Framework;

namespace Sample.Tests;

[TestFixture]
public class SourceTests
{
    public static IEnumerable Cases
    {
        get
        {
            yield return new TestCaseData(2, 3)
                .Returns(5)
                .SetName("adds-positive-values")
                .SetCategory("Math");
            yield return new TestCaseData(-1, 1)
                .Returns(0)
                .SetName("adds-across-zero")
                .SetCategory("Math");
        }
    }

    [TestCaseSource(nameof(Cases))]
    public int Add(int left, int right) => left + right;
}
