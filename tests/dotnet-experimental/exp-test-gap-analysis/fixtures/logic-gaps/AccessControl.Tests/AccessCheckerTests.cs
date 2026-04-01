using Microsoft.VisualStudio.TestTools.UnitTesting;
using AccessControl;

namespace AccessControl.Tests;

[TestClass]
public sealed class AccessCheckerTests
{
    // -- GetPermission tests --
    // Tests the happy path for Admin and User, but never checks Editor on system resources,
    // never verifies Guest access denial, and never checks CanWrite for User role.
    // A flip of CanRead/CanWrite for Editor+system would survive.
    // Removing the Guest denial branch would survive.

    [TestMethod]
    public void GetPermission_Admin_CanReadAndWrite()
    {
        var checker = new AccessChecker();
        var perm = checker.GetPermission(Role.Admin, "documents/report.pdf");
        Assert.IsTrue(perm.CanRead);
        Assert.IsTrue(perm.CanWrite);
    }

    [TestMethod]
    public void GetPermission_Admin_SystemResource_CanReadAndWrite()
    {
        var checker = new AccessChecker();
        var perm = checker.GetPermission(Role.Admin, "sys/config");
        Assert.IsTrue(perm.CanRead);
        Assert.IsTrue(perm.CanWrite);
    }

    [TestMethod]
    public void GetPermission_Editor_NormalResource_CanReadAndWrite()
    {
        var checker = new AccessChecker();
        var perm = checker.GetPermission(Role.Editor, "documents/report.pdf");
        Assert.IsTrue(perm.CanRead);
        Assert.IsTrue(perm.CanWrite);
    }

    [TestMethod]
    public void GetPermission_User_NormalResource_CanRead()
    {
        var checker = new AccessChecker();
        var perm = checker.GetPermission(Role.User, "documents/report.pdf");
        Assert.IsTrue(perm.CanRead);
    }

    [TestMethod]
    public void GetPermission_NullResource_Throws()
    {
        var checker = new AccessChecker();
        Assert.ThrowsException<ArgumentNullException>(
            () => checker.GetPermission(Role.Admin, null!));
    }

    // -- CanPerform tests --
    // Only tests the read path, never tests writeAccess=true.
    // A mutation flipping the ternary (writeAccess ? CanWrite : CanRead) would survive.

    [TestMethod]
    public void CanPerform_AdminRead_ReturnsTrue()
    {
        var checker = new AccessChecker();
        Assert.IsTrue(checker.CanPerform(Role.Admin, "documents/report.pdf", false));
    }

    [TestMethod]
    public void CanPerform_UserReadNormal_ReturnsTrue()
    {
        var checker = new AccessChecker();
        Assert.IsTrue(checker.CanPerform(Role.User, "documents/report.pdf", false));
    }

    // -- ElevateRole tests --
    // Tests the happy path but never checks null token, empty token, or invalid token.
    // Removing the null/empty check would survive.
    // Also never verifies that a Guest can't be elevated to Editor.

    [TestMethod]
    public void ElevateRole_EditorToAdmin_WithValidToken()
    {
        var checker = new AccessChecker();
        var result = checker.ElevateRole(Role.Editor, "ELEVATE-ADMIN");
        Assert.AreEqual(Role.Admin, result);
    }

    [TestMethod]
    public void ElevateRole_UserToEditor_WithValidToken()
    {
        var checker = new AccessChecker();
        var result = checker.ElevateRole(Role.User, "ELEVATE-EDITOR");
        Assert.AreEqual(Role.Editor, result);
    }
}
