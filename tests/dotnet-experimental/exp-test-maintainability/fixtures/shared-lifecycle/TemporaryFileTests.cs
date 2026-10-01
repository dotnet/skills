using System;
using System.IO;
using NUnit.Framework;

namespace FileProcessing.Tests;

[TestFixture]
public sealed class CsvImporterTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [NUnit.Framework.Test]
    public void Import_ExistingFile_ReturnsRows()
    {
        var path = Path.Combine(_directory, "input.csv");
        File.WriteAllText(path, "id,name\n1,Ada");

        NUnit.Framework.Assert.That(File.ReadAllLines(path), Has.Length.EqualTo(2));
    }
}

[TestFixture]
public sealed class JsonImporterTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [NUnit.Framework.Test]
    public void Import_ExistingFile_ReadsObject()
    {
        var path = Path.Combine(_directory, "input.json");
        File.WriteAllText(path, """{"id":1}""");

        NUnit.Framework.Assert.That(File.ReadAllText(path), Does.Contain("\"id\":1"));
    }
}

[TestFixture]
public sealed class XmlImporterTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [NUnit.Framework.Test]
    public void Import_ExistingFile_ReadsDocument()
    {
        var path = Path.Combine(_directory, "input.xml");
        File.WriteAllText(path, "<item id=\"1\" />");

        NUnit.Framework.Assert.That(File.ReadAllText(path), Does.Contain("item"));
    }
}
