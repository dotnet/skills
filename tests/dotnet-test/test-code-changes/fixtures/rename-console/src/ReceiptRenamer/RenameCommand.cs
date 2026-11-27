namespace ReceiptRenamer;

public static class RenameCommand
{
    public static int Run(IEnumerable<string> paths, TextWriter output)
    {
        foreach (var path in paths)
        {
            var source = Path.GetFullPath(path);
            var filename = Path.GetFileName(source).ToLowerInvariant().Replace(' ', '-');
            var destination = Path.Combine(Path.GetDirectoryName(source)!, filename);
            if (source == destination)
                continue;

            File.Move(source, destination);
            output.WriteLine($"{Path.GetFileName(source)} -> {filename}");
        }

        return 0;
    }
}
