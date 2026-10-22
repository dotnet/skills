namespace CleanTracking;

public static class EntryPoint
{
    public static string Summary()
    {
        return BuildInfo.GitHash;
    }
}
