using System.Runtime.Intrinsics;

public static class AsciiLetters
{
    public static bool StartsWithAsciiLetter(ReadOnlySpan<char> text)
    {
        if (!Vector128.IsHardwareAccelerated || text.Length < Vector128<char>.Count)
        {
            return text.Length != 0 && char.IsAsciiLetter(text[0]);
        }

        Vector128<char> chars = Vector128.Create(text);
        return chars[0] is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
    }
}
