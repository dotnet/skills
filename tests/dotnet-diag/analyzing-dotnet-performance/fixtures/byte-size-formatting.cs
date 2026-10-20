using System;
public readonly struct DataSize:IEquatable<DataSize> { public DataSize(long bytes)=>Bytes=bytes; public long Bytes{get;} public string Format(string format){ if(format.Contains("KB")) format=format.Replace("KB","KiB"); else if(format.Contains("MB")) format=format.Replace("MB","MiB"); return format.Replace("#.##","0.##"); } public bool Equals(DataSize other)=>Bytes==other.Bytes; }
