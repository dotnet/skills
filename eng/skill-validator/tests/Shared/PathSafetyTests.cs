using SkillValidator.Shared;

namespace SkillValidator.Tests;

public class PathSafetyTests
{
    [Fact]
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
            Assert.True(PathSafety.ContainsReparsePoint(linkedRoot, linkedRoot));
            Assert.True(PathSafety.ContainsReparsePoint(
                linkedRoot,
                Path.Combine(linkedRoot, "child.txt")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
