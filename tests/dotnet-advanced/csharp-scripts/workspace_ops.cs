using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

if (args.Length == 0)
{
    Console.Error.WriteLine("An operation is required.");
    return 2;
}

return args[0] switch
{
    "setup-package" => SetupPackage(),
    "setup-project" => SetupProject(),
    "setup-csv" => SetupCsv(),
    "run-python" => RunPython(args[1..]),
    "verify-json" => VerifyJson(args[1..]),
    "verify-project" => VerifyProject(),
    "run-clean" => RunClean(args[1..]),
    "verify-clean" => VerifyClean(args[1..]),
    _ => UnknownOperation(args[0]),
};

static int SetupPackage()
{
    Directory.CreateDirectory(Path.Combine(".eval", "package-src"));
    Directory.CreateDirectory(Path.Combine(".eval", "feed"));

    WriteText(
        Path.Combine(".eval", "package-src", "Eval.Greeting.csproj"),
        """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <PackageId>Eval.Greeting</PackageId>
            <Version>1.0.0</Version>
          </PropertyGroup>
        </Project>
        """);
    WriteText(
        Path.Combine(".eval", "package-src", "Greeter.cs"),
        """
        namespace Eval.Greeting;
        public static class Greeter
        {
            public static string Hello(string name) => $"Hello, {name}!";
        }
        """);
    WriteText(
        "nuget.config",
        """
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <packageSources>
            <clear />
            <add key="eval" value=".eval/feed" />
          </packageSources>
        </configuration>
        """);

    return RunProcess(
        "dotnet",
        "pack",
        Path.Combine(".eval", "package-src", "Eval.Greeting.csproj"),
        "-o",
        Path.Combine(".eval", "feed"),
        "--nologo");
}

static int SetupProject()
{
    Directory.CreateDirectory("NumberLib");
    WriteText(
        Path.Combine("NumberLib", "NumberLib.csproj"),
        ProjectFileContent());
    WriteText(
        Path.Combine("NumberLib", "Answer.cs"),
        AnswerFileContent());
    return 0;
}

static int SetupCsv()
{
    WriteText(
        "input.csv",
        """
        name,score
        Ada,42
        Grace,37
        """);
    return 0;
}

static int RunPython(string[] pythonArgs)
{
    (string FileName, string[] Prefix)[] candidates = OperatingSystem.IsWindows()
        ? [("python", []), ("py", ["-3"]), ("python3", [])]
        : [("python3", []), ("python", [])];

    foreach ((string fileName, string[] prefix) in candidates)
    {
        try
        {
            return RunProcess(fileName, [.. prefix, .. pythonArgs]);
        }
        catch (Win32Exception)
        {
        }
    }

    Console.Error.WriteLine("No Python interpreter was found.");
    return 127;
}

static int VerifyJson(string[] verifyArgs)
{
    if (verifyArgs.Length != 1)
    {
        Console.Error.WriteLine("verify-json requires one output path.");
        return 2;
    }

    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(verifyArgs[0]));
    JsonElement root = document.RootElement;
    if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() != 2)
        return 1;

    JsonElement first = root[0];
    JsonElement second = root[1];
    bool valid =
        first.GetProperty("name").GetString() == "Ada"
        && first.GetProperty("score").GetInt32() == 42
        && second.GetProperty("name").GetString() == "Grace"
        && second.GetProperty("score").GetInt32() == 37;
    return valid ? 0 : 1;
}

static int VerifyProject()
{
    string projectPath = Path.Combine("NumberLib", "NumberLib.csproj");
    string answerPath = Path.Combine("NumberLib", "Answer.cs");
    if (!File.Exists(projectPath) || !File.Exists(answerPath))
        return 1;
    if (File.ReadAllText(projectPath) != PrepareText(ProjectFileContent())
        || File.ReadAllText(answerPath) != PrepareText(AnswerFileContent()))
    {
        return 1;
    }

    string[] libraryFiles = Directory
        .EnumerateFiles("NumberLib", "*", SearchOption.AllDirectories)
        .Select(NormalizePath)
        .Where(path => !path.StartsWith("NumberLib/bin/", StringComparison.Ordinal)
            && !path.StartsWith("NumberLib/obj/", StringComparison.Ordinal))
        .Order(StringComparer.Ordinal)
        .ToArray();
    string[] expectedLibraryFiles =
    [
        "NumberLib/Answer.cs",
        "NumberLib/NumberLib.csproj",
    ];
    if (!libraryFiles.SequenceEqual(expectedLibraryFiles, StringComparer.Ordinal))
        return 1;

    string[] projectFiles = Directory
        .EnumerateFiles(".", "*.csproj", SearchOption.AllDirectories)
        .Select(NormalizePath)
        .Where(path => !path.StartsWith(".eval/", StringComparison.Ordinal)
            && !path.StartsWith(".git/", StringComparison.Ordinal))
        .Order(StringComparer.Ordinal)
        .ToArray();
    return projectFiles.SequenceEqual(
        ["NumberLib/NumberLib.csproj"],
        StringComparer.Ordinal)
        ? 0
        : 1;
}

static int RunClean(string[] cleanArgs)
{
    if (cleanArgs.Length != 1)
    {
        Console.Error.WriteLine("run-clean requires one source path.");
        return 2;
    }

    int runExitCode = RunProcessCaptured(
        "dotnet",
        [cleanArgs[0]],
        out string runOutput,
        out string runError);
    if (runExitCode != 0 || runOutput.Trim() != "cleanup-ok")
    {
        Console.Error.Write(runOutput);
        Console.Error.Write(runError);
        return 1;
    }

    int cleanExitCode = RunProcessCaptured(
        "dotnet",
        ["clean", cleanArgs[0], "--nologo"],
        out string cleanOutput,
        out string cleanError);
    if (cleanExitCode != 0)
    {
        Console.Error.Write(cleanOutput);
        Console.Error.Write(cleanError);
        return 1;
    }

    int verification = VerifyClean(cleanArgs);
    if (verification == 0)
        Console.WriteLine("cleanup-ok");
    return verification;
}

static int VerifyClean(string[] cleanArgs)
{
    if (cleanArgs.Length != 1 || !File.Exists(cleanArgs[0]))
        return 2;
    if (Directory
        .EnumerateDirectories(".", "*", SearchOption.AllDirectories)
        .Select(NormalizePath)
        .Any(path =>
        {
            if (path.StartsWith(".eval/", StringComparison.Ordinal)
                || path.StartsWith(".git/", StringComparison.Ordinal))
            {
                return false;
            }
            string name = Path.GetFileName(path);
            return name.Equals("bin", StringComparison.OrdinalIgnoreCase)
                || name.Equals("obj", StringComparison.OrdinalIgnoreCase);
        }))
    {
        return 1;
    }

    bool hasProject = Directory
        .EnumerateFiles(".", "*.csproj", SearchOption.AllDirectories)
        .Select(NormalizePath)
        .Any(path => !path.StartsWith(".eval/", StringComparison.Ordinal)
            && !path.StartsWith(".git/", StringComparison.Ordinal));
    return hasProject ? 1 : 0;
}

static int RunProcess(string fileName, params string[] processArgs)
{
    var startInfo = new ProcessStartInfo(fileName)
    {
        UseShellExecute = false,
    };
    foreach (string argument in processArgs)
        startInfo.ArgumentList.Add(argument);

    using Process process = Process.Start(startInfo)
        ?? throw new InvalidOperationException($"Could not start {fileName}.");
    process.WaitForExit();
    return process.ExitCode;
}

static int RunProcessCaptured(
    string fileName,
    string[] processArgs,
    out string standardOutput,
    out string standardError)
{
    var startInfo = new ProcessStartInfo(fileName)
    {
        RedirectStandardError = true,
        RedirectStandardOutput = true,
        UseShellExecute = false,
    };
    foreach (string argument in processArgs)
        startInfo.ArgumentList.Add(argument);

    using Process process = Process.Start(startInfo)
        ?? throw new InvalidOperationException($"Could not start {fileName}.");
    standardOutput = process.StandardOutput.ReadToEnd();
    standardError = process.StandardError.ReadToEnd();
    process.WaitForExit();
    return process.ExitCode;
}

static void WriteText(string path, string content)
{
    string? directory = Path.GetDirectoryName(path);
    if (!string.IsNullOrEmpty(directory))
        Directory.CreateDirectory(directory);
    File.WriteAllText(path, PrepareText(content));
}

static string PrepareText(string content) =>
    content.Replace("\r\n", "\n") + "\n";

static string NormalizePath(string path)
{
    string normalized = path.Replace('\\', '/');
    return normalized.StartsWith("./", StringComparison.Ordinal)
        ? normalized[2..]
        : normalized;
}

static string ProjectFileContent() =>
    """
    <Project Sdk="Microsoft.NET.Sdk">
      <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
      </PropertyGroup>
    </Project>
    """;

static string AnswerFileContent() =>
    """
    namespace NumberLib;
    public static class Answer
    {
        public static int Value => 42;
    }
    """;

static int UnknownOperation(string operation)
{
    Console.Error.WriteLine($"Unknown operation: {operation}");
    return 2;
}
