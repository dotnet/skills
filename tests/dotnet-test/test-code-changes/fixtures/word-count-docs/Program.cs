var path = args.Single(arg => arg != "--verbose");
var text = File.ReadAllText(path);

if (args.Contains("--verbose"))
    Console.Error.WriteLine($"Counting words in {Path.GetFullPath(path)}");

Console.WriteLine(text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length);
