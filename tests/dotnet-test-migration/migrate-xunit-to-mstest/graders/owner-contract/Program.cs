using System.Reflection;
using System.Text.RegularExpressions;

var assembly = Assembly.Load("TestProject");
var source = Regex.Replace(File.ReadAllText("OwnerTests.cs"),
    @"//[^\n]*|/\*.*?\*/", "", RegexOptions.Singleline);
source = Regex.Replace(source, @"\s+", "");
var assertions = new Dictionary<string, string>
{
    ["InheritsAlice"] = "Assert.AreEqual(4,2+2)",
    ["DeduplicatesAlice"] = "Assert.AreEqual(6,3+3)",
    ["InheritsClassAlice"] = "Assert.AreEqual(8,4+4)",
};
var expected = new HashSet<string>
{
    "MigrationFixture.AssemblyOwnerTests.InheritsAlice",
    "MigrationFixture.ClassOwnerTests.DeduplicatesAlice",
    "MigrationFixture.ClassOwnerTests.InheritsClassAlice",
};
const string prefix = "Microsoft.VisualStudio.TestTools.UnitTesting.";
if (assembly.GetCustomAttributesData().Any(a => a.AttributeType.FullName == prefix + "OwnerAttribute"))
    throw new InvalidOperationException("Owner must not be on the assembly.");
foreach (var type in assembly.GetTypes())
{
    if (type.GetCustomAttributesData().Any(a => a.AttributeType.FullName == prefix + "OwnerAttribute"))
        throw new InvalidOperationException("Owner must not be on a class.");
    foreach (var method in type.GetMethods())
    {
        if (!method.GetCustomAttributesData().Any(a => a.AttributeType.FullName == prefix + "TestMethodAttribute"))
            continue;
        if (!type.GetCustomAttributesData().Any(a => a.AttributeType.FullName == prefix + "TestClassAttribute"))
            throw new InvalidOperationException("Test class is not discoverable.");
        if (!expected.Remove(type.FullName + "." + method.Name))
            throw new InvalidOperationException("Unexpected or duplicate migrated test.");
        var signature = "void" + method.Name + "()";
        var assertion = assertions[method.Name];
        if (!source.Contains(signature + "=>" + assertion + ";", StringComparison.Ordinal)
            && !source.Contains(signature + "{" + assertion + ";}", StringComparison.Ordinal))
            throw new InvalidOperationException("Original test assertion was changed or lost.");
        var owners = method.GetCustomAttributesData()
            .Where(a => a.AttributeType.FullName == prefix + "OwnerAttribute").ToArray();
        if (owners.Length != 1 || !Equals(owners[0].ConstructorArguments.Single().Value, "alice"))
            throw new InvalidOperationException("Each method needs exactly one effective Owner alice.");
        if (method.GetCustomAttributesData().Any(a => a.AttributeType.FullName == prefix + "IgnoreAttribute"))
            throw new InvalidOperationException("Required test was ignored.");
    }
}
if (expected.Count != 0)
    throw new InvalidOperationException("Migrated tests were lost.");
Console.WriteLine("OWNER_CONTRACT:3 methods, one alice each");
