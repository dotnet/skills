namespace SkillValidator.Tests;

internal static class SymlinkTestHelper
{
    internal static bool TryCreateFile(string path, string target)
    {
        try
        {
            File.CreateSymbolicLink(path, target);
            return true;
        }
        catch (Exception ex) when (
            ex is IOException
            or UnauthorizedAccessException
            or PlatformNotSupportedException
            or NotSupportedException)
        {
            return false;
        }
    }

    internal static bool TryCreateDirectory(string path, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(path, target);
            return true;
        }
        catch (Exception ex) when (
            ex is IOException
            or UnauthorizedAccessException
            or PlatformNotSupportedException
            or NotSupportedException)
        {
            return false;
        }
    }
}
