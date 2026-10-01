using System; using System.Collections.Generic; using System.Text.RegularExpressions;
public sealed class Vocabulary {
 private readonly HashSet<string> _words=new(StringComparer.CurrentCultureIgnoreCase); private readonly List<Regex> _rules=new();
 public void Add(string pattern)=>_rules.Add(new Regex(pattern,RegexOptions.Compiled)); public bool Contains(string word)=>_words.Contains(word);
}
