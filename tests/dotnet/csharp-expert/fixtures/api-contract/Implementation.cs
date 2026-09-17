internal static class PortNumber
{
    public static bool TryParse(string? text, out int port)
    {
        return int.TryParse(text, out port);
    }
}
