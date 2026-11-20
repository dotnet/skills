using System.Xml.Linq;

if (args.Length != 1)
{
    throw new ArgumentException("Usage: AddAnalyzer <project.csproj>");
}

const string package = "Microsoft.CodeAnalysis.NetAnalyzers";
var document = XDocument.Load(args[0]);
var project = document.Root ?? throw new InvalidDataException("The project has no root element.");
var ns = project.Name.Namespace;
if (project.Descendants(ns + "PackageReference").Any(reference =>
    string.Equals((string?)reference.Attribute("Include"), package, StringComparison.OrdinalIgnoreCase)))
{
    return;
}

project.Add(new XElement(ns + "ItemGroup",
    new XElement(ns + "PackageReference",
        new XAttribute("Include", package),
        new XAttribute("Version", "10.0.100"),
        new XAttribute("PrivateAssets", "all"))));
document.Save(args[0]);
Console.WriteLine($"Added {package} to {args[0]}.");
