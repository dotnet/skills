using ContosoUniversity.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ContosoUniversity.UnitTests;

[TestClass]
public class StudentServiceTests
{
    private StudentService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _service = new StudentService();
    }

    [TestMethod]
    public void Enroll_ValidStudent_ReturnsSuccess()
    {
        var result = _service.Enroll("Alice", "Smith", 20);
        Assert.IsTrue(result.Success);
        Assert.IsNotNull(result.StudentId);
    }

    [TestMethod]
    public void Enroll_EmptyName_ReturnsFalse()
    {
        var result = _service.Enroll("", "Smith", 20);
        Assert.IsFalse(result.Success);
        Assert.AreEqual("Name is required.", result.Message);
    }

    [TestMethod]
    public void FindById_ExistingStudent_ReturnsStudent()
    {
        _service.Enroll("Bob", "Jones", 22);
        var student = _service.FindById(1);
        Assert.IsNotNull(student);
        Assert.AreEqual("Bob", student.FirstName);
    }

    [TestMethod]
    public void FindById_NonExistent_ReturnsNull()
    {
        var student = _service.FindById(999);
        Assert.IsNull(student);
    }

    [TestMethod]
    public void Search_EmptyQuery_ReturnsEmpty()
    {
        _service.Enroll("Carol", "White", 19);
        var results = _service.Search("");
        Assert.AreEqual(0, results.Count);
    }

    // Note: No tests for CalculateGpa — this is intentional to create a coverage gap
    // for the coverage-analysis skill to detect as a risk hotspot.
}
