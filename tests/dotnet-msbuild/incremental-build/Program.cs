namespace IncrementalBroken;

public static class EntryPoint
{
    public static string Summary()
    {
        return $"{BuildInfo.Stamp}:{BuildInfo.GitHash}";
    }
}
