using System.Collections.Generic;
public static class Truncation { public static string FixedLength(string value,int length)=>value[..length].TrimEnd(); public static string FixedCharacters(string value,int length)=>value.AsSpan(0,length).ToString(); public static string FixedWords(string value,int length)=>value[..length].TrimEnd(); }
public static class Symbols { public static readonly List<char>[] Groups=[new(){'.',',','!'}]; }
