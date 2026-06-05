using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
// NOTE: This file was compiled against .NET 9 RC2 which had
// String.Trim(params ReadOnlySpan<char>) overloads that were
// removed in GA. Recompiling against GA will change overload resolution.
class StringUtils
{
    public string CleanInput(string input)
    {
        ReadOnlySpan<char> trimChars = [' ', '\t', '\n'];
        return input.Trim(trimChars);
    }
}
[InlineArray(1048577)]
struct LargeBuffer { private byte _element0; }
