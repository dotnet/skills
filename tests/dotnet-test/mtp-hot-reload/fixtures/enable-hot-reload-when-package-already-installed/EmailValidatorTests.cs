using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace Contoso.Validation.Tests;

[TestClass]
public class EmailValidatorTests
{
    [TestMethod]
    public void Validate_ValidEmail_ReturnsTrue()
    {
        Assert.IsTrue(IsValidEmail("user@example.com"));
    }

    [TestMethod]
    public void Validate_MissingAtSign_ReturnsFalse()
    {
        Assert.IsFalse(IsValidEmail("userexample.com"));
    }

    [TestMethod]
    public void Validate_EmptyString_ReturnsFalse()
    {
        Assert.IsFalse(IsValidEmail(""));
    }

    private static bool IsValidEmail(string email)
    {
        return !string.IsNullOrEmpty(email);
    }
}
