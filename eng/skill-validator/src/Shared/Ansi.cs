namespace SkillValidator.Shared;

/// <summary>
/// ANSI escape code constants for terminal output styling.
/// </summary>
internal static class Ansi
{
    internal const string Reset = "\x1b[0m";
    internal const string Bold = "\x1b[1m";
    internal const string Dim = "\x1b[2m";

    internal const string Red = "\x1b[31m";
    internal const string Green = "\x1b[32m";
    internal const string Yellow = "\x1b[33m";
    internal const string Cyan = "\x1b[36m";

    internal const string BoldRed = "\x1b[31;1m";

    internal const string ClearLine = "\x1b[K";
}
