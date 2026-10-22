using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using BlazorComponentReadiness.Validator.IO;

internal static class SourceMemberReadTests
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static string _reader = "";
    private static int _checks;

    public static void Run()
    {
        _reader = Environment.GetEnvironmentVariable("READINESS_MEMBER_READER_DLL")
            ?? throw new InvalidOperationException("Build the separate reader externally and set READINESS_MEMBER_READER_DLL.");
        var scratch = Environment.GetEnvironmentVariable("READINESS_TEST_ROOT")
            ?? throw new InvalidOperationException("Member-reader controls require external READINESS_TEST_ROOT.");
        if (!File.Exists(_reader)) throw new FileNotFoundException("Built member reader is missing.", _reader);
        var root = Path.Combine(scratch, "member-reader-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        const string method = """"
            [System.Obsolete]
            public string Render(int value)
            {
                var regular = "quoted \" } {";
                var verbatim = @"{ ""inside"" }";
                var interpolation = $"{{literal}} {value /* } */}";
                var raw = """raw } {""";
                var interpolatedRaw = $$"""value={{value}} { }""";
                char close = '}';
                // } is not the closing token
                /* { nested-looking braces } */
                if (value > 0) { return "later branch"; }
                return raw;
            }
            """";
        const string overload = "public string Render(string value) => value;";
        const string constructor = "public Sample() => System.Console.WriteLine(\"constructor }\");";
        var source = "class Sample\n{\n" + method + "\n" + overload + "\n" + constructor + "\n}\n";
        var fixture = Capture(root, "complete", Utf8.GetBytes(source));
        CheckPacket(fixture, Line(source, "var verbatim"), method, "method", "whole.json");
        CheckPacket(fixture, Line(source, overload), overload, "method", "overload.json");
        CheckPacket(fixture, Line(source, constructor), constructor, "constructor", "constructor.json");
        var prior = File.ReadAllBytes(Path.Combine(fixture.Root, "whole.json"));
        Reject(fixture, 6, "whole.json", "existing output");
        Assert(prior.SequenceEqual(File.ReadAllBytes(Path.Combine(fixture.Root, "whole.json"))), "non-overwrite");
        Reject(fixture, 1, "outside.json", "outside any supported member");
        Reject(fixture, 999, "past.json", "outside file");
        Reject(fixture, 6, "../escaped.json", "output traversal");
        Reject(fixture, 6, Path.Combine(fixture.Root, "absolute.json"), "absolute output");
        Reject(fixture, 6, "packet.json:stream", "alternate stream");
        Reject(fixture, 6, "captured/inside.json", "output inside source");
        Reject(fixture, 6, "unsafe-source.json", "source traversal", sourcePath: "../Member.cs");
        Reject(fixture, 6, "missing-source.json", "uncaptured source", sourcePath: "src/Other.cs");
        Reject(fixture, 6, "wrong-receipt.json", "receipt digest mismatch", expectedDigest: new string('0', 64));

        foreach (var newline in new[] { "\n", "\r\n" })
        {
            var exact = "[System.Obsolete]" + newline + "public string Text()" + newline +
                "{" + newline + "    return \"caf\u00E9 \U0001F680\";" + newline + "}";
            var full = "class Unicode" + newline + "{" + newline + exact + newline + "}" + newline;
            foreach (var bom in new[] { false, true })
            {
                byte[] bytes = [.. bom ? new byte[] { 0xEF, 0xBB, 0xBF } : Array.Empty<byte>(), .. Utf8.GetBytes(full)];
                var control = Capture(root, "utf8-" + _checks, bytes);
                CheckPacket(control, 5, exact, "method", "exact.json");
            }
        }
        var blockConstructor = "public Sample(int value)\n{\n    System.Console.WriteLine(value);\n}";
        var ctorSource = "class Sample\n{\n" + blockConstructor + "\n}\n";
        CheckPacket(Capture(root, "block-ctor", Utf8.GetBytes(ctorSource)), 5, blockConstructor, "constructor", "packet.json");
        const string unresolved = "public MissingType Read() => MissingCall();";
        CheckPacket(Capture(root, "syntax-only", Utf8.GetBytes("class C\n{\n" + unresolved + "\n}\n")),
            3, unresolved, "method", "packet.json");
        foreach (var (name, code, line) in new[]
        {
            ("malformed", "class C\n{\n void Read( { }\n}\n", 3),
            ("property", "class C\n{\n int Value { get { return 1; } }\n}\n", 3),
            ("abstract", "abstract class C\n{\n public abstract void Read();\n}\n", 3),
            ("ambiguous", "class C { void A() {} void B() {} }\n", 1),
            ("inactive", "class C\n{\n#if NEVER\n void Hidden() {}\n#endif\n}\n", 4),
            ("inactive-body", "class C\n{\n void Read() {\n#if NEVER\n junk } {\n#endif\n }\n}\n", 5)
        })
            Reject(Capture(root, name, Utf8.GetBytes(code)), line, "packet.json", name);
        Reject(Capture(root, "invalid-utf8", [0xFF]), 1, "packet.json", "strict UTF-8");
        Reject(Capture(root, "packet-cap", Utf8.GetBytes(
            "class C\n{\n void Read() { /*" + new string('a', 13_000) + "*/ }\n}\n")),
            3, "packet.json", "whole serialized packet cap");
        Reject(Capture(root, "escaped-packet-cap", Utf8.GetBytes(
            "class C\n{\n void Read() { /*" + new string('\u00E9', 2500) + "*/ }\n}\n")),
            3, "packet.json", "JSON escaping exceeds cap despite small UTF-8 source");
        var seedMethod = "void Read() { /*" + new string('a', 1000) + "*/ }";
        var seedBytes = CheckPacket(Capture(root, "packet-boundary-seed",
            Utf8.GetBytes("class C\n{\n" + seedMethod + "\n}\n")), 3, seedMethod, "method", "packet.json");
        using (var seed = JsonDocument.Parse(seedBytes))
        {
            var padding = 1000 + 12 * 1024 - seedBytes.Length;
            var endByte = seed.RootElement.GetProperty("end_byte_exclusive").GetInt32();
            var endColumn = seed.RootElement.GetProperty("end_column_exclusive").GetInt32();
            padding -= Digits(endByte + padding - 1000) - Digits(endByte) +
                       Digits(endColumn + padding - 1000) - Digits(endColumn);
            var exactMethod = "void Read() { /*" + new string('a', padding) + "*/ }";
            var exactBytes = CheckPacket(Capture(root, "at-packet-cap",
                Utf8.GetBytes("class C\n{\n" + exactMethod + "\n}\n")), 3, exactMethod, "method", "packet.json");
            Assert(exactBytes.Length == 12 * 1024, "exact entire packet ceiling including newline");
            Reject(Capture(root, "over-packet-cap", Utf8.GetBytes("class C\n{\n" +
                "void Read() { /*" + new string('a', padding + 1) + "*/ }\n}\n")),
                3, "packet.json", "one byte over serialized packet ceiling");
        }
        const string small = "class C\n{\n void Read() {}\n}\n";
        var atCap = Utf8.GetBytes(small + "/*" + new string(' ', 8 * 1024 * 1024 - small.Length - 4) + "*/");
        Assert(atCap.Length == 8 * 1024 * 1024, "source cap construction");
        CheckPacket(Capture(root, "at-source-cap", atCap), 3, "void Read() {}", "method", "packet.json");
        Reject(Capture(root, "over-source-cap", [.. atCap, (byte)' ']), 3, "packet.json", "source byte cap");
        var sourceMutation = Capture(root, "source-mutation", Utf8.GetBytes(small));
        File.AppendAllText(sourceMutation.Source, " ");
        Reject(sourceMutation, 3, "packet.json", "changed source bytes");
        var receiptMutation = Capture(root, "receipt-mutation", Utf8.GetBytes(small));
        File.AppendAllText(Path.Combine(receiptMutation.Root, "capture.json"), "\n");
        Reject(receiptMutation, 3, "packet.json", "changed receipt bytes");
        var duplicate = Capture(root, "ambiguous-binding", Utf8.GetBytes(small));
        using (var receipt = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(duplicate.Root, "capture.json"))))
        {
            var original = receipt.RootElement.GetRawText();
            var row = receipt.RootElement.GetProperty("source_artifacts")[0].GetRawText();
            var replacement = original.Replace("\"source_artifacts\":[" + row + "]",
                "\"source_artifacts\":[" + row + "," + row + "]", StringComparison.Ordinal);
            Assert(replacement != original, "duplicate capture construction");
            File.WriteAllText(Path.Combine(duplicate.Root, "capture.json"), replacement, Utf8);
        }
        Reject(duplicate, 3, "packet.json", "ambiguous capture binding",
            expectedDigest: ContractJson.RawDigest(File.ReadAllBytes(Path.Combine(duplicate.Root, "capture.json"))).Value);

        var linked = Capture(root, "linked", Utf8.GetBytes(small));
        var realOutput = Path.Combine(linked.Root, "real-output");
        Directory.CreateDirectory(realOutput);
        LinkDirectory(Path.Combine(linked.Root, "linked-output"), realOutput);
        Reject(linked, 3, "linked-output/packet.json", "linked output");
        var realSource = Path.Combine(linked.Root, "real-source");
        Directory.Move(Path.Combine(linked.Root, "captured", "src"), realSource);
        LinkDirectory(Path.Combine(linked.Root, "captured", "src"), realSource);
        Reject(linked, 3, "linked-source.json", "linked source");
        var rootLink = Path.Combine(root, "linked-root");
        LinkDirectory(rootLink, fixture.Root);
        Reject(fixture with { Root = rootLink }, 6, "linked-root.json", "linked root");
        Console.WriteLine($"Member-reader controls passed: {_checks}; retained scratch: {root}");
    }

    private sealed record Fixture(string Root, string Source, string Digest);

    private static Fixture Capture(string root, string name, byte[] bytes)
    {
        var directory = Path.Combine(root, name);
        var source = Path.Combine(directory, "captured", "src", "Member.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllBytes(source, bytes);
        using (var archive = ZipFile.Open(Path.Combine(directory, "source.zip"), ZipArchiveMode.Create))
        using (var stream = archive.CreateEntry("src/Member.cs", CompressionLevel.Fastest).Open())
            stream.Write(bytes);
        SourceArchiveCaptureService.Capture(directory, "source.zip", "captured", null, "zip",
            "https://github.com/example-org/synthetic-reader", new string('a', 40),
            "Original synthetic syntax control", "low", "https://example.org/source.zip",
            ["src/Member.cs"], null, "capture.json");
        return new(directory, source, ContractJson.RawDigest(File.ReadAllBytes(Path.Combine(directory, "capture.json"))).Value);
    }

    private static byte[] CheckPacket(Fixture fixture, int line, string expected, string kind, string output)
    {
        var result = Invoke(fixture, line, output);
        Assert(result.ExitCode == 0, "successful reader: " + result.Error);
        var bytes = File.ReadAllBytes(Path.Combine(fixture.Root, output));
        Assert(bytes.Length <= 12 * 1024, "complete serialized packet cap");
        using var document = JsonDocument.Parse(bytes);
        var packet = document.RootElement;
        Assert(packet.GetProperty("original_text").GetString() == expected, "exact complete original member");
        Assert(packet.GetProperty("member_kind").GetString() == kind, "member kind");
        Assert(packet.GetProperty("selected_line").GetInt32() == line, "selected line");
        Assert(packet.GetProperty("receipt_sha256").GetProperty("value").GetString() == fixture.Digest, "receipt digest");
        var source = File.ReadAllBytes(fixture.Source);
        Assert(packet.GetProperty("source_sha256").GetProperty("value").GetString() == ContractJson.RawDigest(source).Value, "source digest");
        Assert(packet.GetProperty("member_text_sha256").GetProperty("value").GetString() ==
            ContractJson.RawDigest(Utf8.GetBytes(expected)).Value, "member digest");
        var start = packet.GetProperty("start_byte").GetInt32();
        var end = packet.GetProperty("end_byte_exclusive").GetInt32();
        Assert(source.AsSpan(start, end - start).SequenceEqual(Utf8.GetBytes(expected)), "original source byte slice");
        var text = Utf8.GetString(source);
        var startLine = 1 + text[..text.IndexOf(expected, StringComparison.Ordinal)].Count(character => character == '\n');
        Assert(packet.GetProperty("start_line").GetInt32() == startLine, "physical start line");
        Assert(packet.GetProperty("end_line").GetInt32() == startLine + expected.Count(character => character == '\n'), "physical end line");
        var second = output + ".repeat.json";
        var repeat = Invoke(fixture, line, second);
        Assert(repeat.ExitCode == 0, "repeat extraction: " + repeat.Error);
        Assert(bytes.SequenceEqual(File.ReadAllBytes(Path.Combine(fixture.Root, second))), "deterministic packet bytes");
        _checks++;
        return bytes;
    }

    private static void Reject(Fixture fixture, int line, string output, string reason,
        string sourcePath = "src/Member.cs", string? expectedDigest = null)
    {
        var result = Invoke(fixture, line, output, sourcePath, expectedDigest);
        Assert(result.ExitCode != 0 && !string.IsNullOrWhiteSpace(result.Error), "explicit failure: " + reason);
        if (output != "whole.json")
            Assert(!File.Exists(Path.Combine(fixture.Root, output)), "no partial packet: " + reason);
        _checks++;
    }

    private static (int ExitCode, string Error) Invoke(Fixture fixture, int line, string output,
        string sourcePath = "src/Member.cs", string? expectedDigest = null)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var arg in new[] { _reader, "--root", fixture.Root, "--receipt", "capture.json",
                     "--expected-sha256", expectedDigest ?? fixture.Digest, "--source-path", sourcePath,
                     "--line", line.ToString(System.Globalization.CultureInfo.InvariantCulture), "--output", output })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Reader process did not start.");
        var stderr = process.StandardError.ReadToEndAsync();
        var stdout = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit(60_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new InvalidOperationException("Member reader exceeded the offline process bound.");
        }
        Task.WaitAll(stderr, stdout);
        return (process.ExitCode, stderr.Result);
    }

    private static void LinkDirectory(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe")
            { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "/d", "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Junction creation did not start.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert(process.ExitCode == 0, "junction control setup: " + output + error);
    }

    private static int Line(string source, string needle) =>
        1 + source[..source.IndexOf(needle, StringComparison.Ordinal)].Count(character => character == '\n');

    private static int Digits(int value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture).Length;

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Member-reader control failed: " + message);
    }
}
