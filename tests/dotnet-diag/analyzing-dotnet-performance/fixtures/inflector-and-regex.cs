using System.Text.RegularExpressions;
public static partial class CaseTransforms {
 private static readonly Regex Words=new("([a-z])([A-Z])",RegexOptions.Compiled);
 private static readonly Regex Dashes=new("[- ]+",RegexOptions.Compiled);
 public static string Underscore(string value)=>Dashes.Replace(Words.Replace(value,"$1_$2"),"_").ToLower();
 public static string Pascalize(string value)=>Words.Replace(value,m=>m.Value.ToUpper());
 [GeneratedRegex("^[a-z]+$")] private static partial Regex Lowercase();
}
