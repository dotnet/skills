using BuildTaskContracts;
using System.IO;

if (args.Length != 1)
{
    return 1;
}

Directory.CreateDirectory(Path.GetDirectoryName(args[0])!);
File.WriteAllText(args[0], ManifestSpec.Header);
return 0;
