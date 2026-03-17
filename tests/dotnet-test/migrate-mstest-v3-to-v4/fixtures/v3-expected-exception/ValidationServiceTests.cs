using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MyApp.Tests;

[TestClass]
public class ValidationServiceTests
{
    [ExpectedException(typeof(ArgumentNullException))]
    [TestMethod]
    public void Validate_NullInput_ThrowsArgumentNull()
    {
        Validate(null);
    }

    [ExpectedException(typeof(InvalidOperationException))]
    [TestMethod]
    public void Process_InvalidState_ThrowsInvalidOperation()
    {
        Process();
    }

    [TestMethod]
    public void Validate_ValidInput_Succeeds()
    {
        Assert.IsTrue(true);
    }

    private static void Validate(string input)
    {
        if (input == null)
            throw new ArgumentNullException(nameof(input));
    }

    private static void Process()
    {
        throw new InvalidOperationException("Invalid state");
    }
}
