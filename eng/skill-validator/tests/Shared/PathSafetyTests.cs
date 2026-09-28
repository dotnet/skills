using SkillValidator.Shared;

namespace SkillValidator.Tests;

[TestClass]
public class PathSafetyTests
{
    [TestMethod]
    public void ContainsReparsePointRejectsLinkedAllowedRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"linked-allowed-root-{Guid.NewGuid():N}");
        var target = Path.Combine(root, "target");
        var linkedRoot = Path.Combine(root, "linked-root");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "child.txt"), "content");
        if (!SymlinkTestHelper.TryCreateDirectory(linkedRoot, target))
        {
            Directory.Delete(root, true);
            return;
        }

        try
        {
            Assert.IsTrue(PathSafety.ContainsReparsePoint(linkedRoot, linkedRoot));
            Assert.IsTrue(PathSafety.ContainsReparsePoint(
                linkedRoot,
                Path.Combine(linkedRoot, "child.txt")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
