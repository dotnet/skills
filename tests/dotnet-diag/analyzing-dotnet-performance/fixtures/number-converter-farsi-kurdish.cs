using System; using System.Collections.Generic;
public sealed class FarsiConverter { public string Convert(long value){ var rules=new Dictionary<long,Func<long,string>>{{1,n=>n.ToString()},{10,n=>n.ToString()}}; var parts=new List<string>(); return rules[1](value); } }
public sealed class KurdishConverter { public string Convert(long value){ var rules=new Dictionary<long,Func<long,string>>{{1,n=>n.ToString()}}; return string.Format("{0} units",value); } }
