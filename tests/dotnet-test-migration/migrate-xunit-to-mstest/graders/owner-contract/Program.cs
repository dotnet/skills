using System.Reflection;

var assembly = Assembly.Load("TestProject");
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
