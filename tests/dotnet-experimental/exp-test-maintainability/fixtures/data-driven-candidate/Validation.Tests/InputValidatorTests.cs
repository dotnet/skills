using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Validation.Tests;

[TestClass]
public sealed class InputValidatorTests
{
    [TestMethod]
    public void Validate_EmptyString_ReturnsFalse()
    {
        var validator = new InputValidator();
        var result = validator.Validate("");
        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("Input cannot be empty", result.ErrorMessage);
    }

    [TestMethod]
    public void Validate_TooShort_ReturnsFalse()
    {
        var validator = new InputValidator();
        var result = validator.Validate("ab");
        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("Input must be at least 3 characters", result.ErrorMessage);
    }

    [TestMethod]
    public void Validate_TooLong_ReturnsFalse()
    {
        var validator = new InputValidator();
        var result = validator.Validate(new string('x', 256));
        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("Input must not exceed 255 characters", result.ErrorMessage);
    }

    [TestMethod]
    public void Validate_ContainsSpecialChars_ReturnsFalse()
    {
        var validator = new InputValidator();
        var result = validator.Validate("hello<script>");
        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("Input contains invalid characters", result.ErrorMessage);
    }

    [TestMethod]
    public void Validate_ValidInput_ReturnsTrue()
    {
        var validator = new InputValidator();
        var result = validator.Validate("hello-world");
        Assert.IsTrue(result.IsValid);
        Assert.IsNull(result.ErrorMessage);
    }

    [TestMethod]
    public void Validate_MinLength_ReturnsTrue()
    {
        var validator = new InputValidator();
        var result = validator.Validate("abc");
        Assert.IsTrue(result.IsValid);
        Assert.IsNull(result.ErrorMessage);
    }

    [TestMethod]
    public void Validate_MaxLength_ReturnsTrue()
    {
        var validator = new InputValidator();
        var result = validator.Validate(new string('a', 255));
        Assert.IsTrue(result.IsValid);
        Assert.IsNull(result.ErrorMessage);
    }

    [TestMethod]
    public void Validate_Null_ReturnsFalse()
    {
        var validator = new InputValidator();
        var result = validator.Validate(null);
        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("Input cannot be empty", result.ErrorMessage);
    }

    [TestMethod]
    public void Validate_WhitespaceOnly_ReturnsFalse()
    {
        var validator = new InputValidator();
        var result = validator.Validate("   ");
        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("Input cannot be empty", result.ErrorMessage);
    }
}
