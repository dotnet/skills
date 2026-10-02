using System.Collections.Frozen; using System.Collections.Generic; using System.Linq;
public readonly struct UnitPrefix { public UnitPrefix(char symbol)=>Symbol=symbol; public char Symbol{get;} }
public static class MetricNumerals { private static readonly FrozenDictionary<char,int> Powers=new Dictionary<char,int>{{'k',3},{'M',6}}.ToFrozenDictionary(); private static readonly UnitPrefix[] Prefixes=[new('k'),new('M')]; public static string Normalize(string value)=>Prefixes.Aggregate(value,(text,prefix)=>text.Replace(prefix.Symbol.ToString(),"")); }
