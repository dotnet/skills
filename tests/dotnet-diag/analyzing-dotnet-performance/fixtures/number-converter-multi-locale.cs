using System.Collections.Generic;
public sealed class SwedishConverter { private static readonly Dictionary<string,int> Units=new(){{"ett",1}}; public bool Has(string value)=>value.StartsWith("ett")||value.EndsWith("ton"); }
public sealed class FrenchConverter { public string Convert(IEnumerable<string> values){ var parts=new List<string>(); foreach(var value in values) parts.Add(value); return string.Join("-",parts); } }
public sealed class HungarianConverter { private readonly Dictionary<int,string> _runtimeCache=new(); public string Convert(int value)=>_runtimeCache.TryGetValue(value,out var text)?text:(_runtimeCache[value]=value.ToString()); }
