using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using SkillValidator.Evaluate;

namespace SkillValidator.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ProgramGraderTests : IDisposable
{
    private readonly DirectoryInfo _workspace = Directory.CreateTempSubdirectory("program-grader-test-");

    public void Dispose() => _workspace.Delete(recursive: true);

    private static Assertion Program(string script, int timeout = 30) =>
        new(AssertionType.Program, CommandArgs: new CommandAssertionArgs(
            "node", ExpectedExitCode: 0, Timeout: timeout, ArgumentList: ["-e", script]));

    [TestMethod]
    public void ParsesProgramArgsAndDefaultTimeout()
    {
        var config = EvalSchema.ParseEvalConfigFlexible("""
            stimuli:
              - name: Check execution
                prompt: Inspect the launch.
                graders:
                  - type: program
                    config:
                      program: node
                      args: ["check.cjs", "an argument with spaces"]
            """);

        var assertion = Assert.ContainsSingle(Assert.ContainsSingle(config!.Scenarios).Assertions!);
        Assert.AreEqual(AssertionType.Program, assertion.Type);
        Assert.AreEqual("node", assertion.CommandArgs!.CommandToRun);
        Assert.AreSequenceEqual(["check.cjs", "an argument with spaces"], assertion.CommandArgs.ArgumentList!);
        Assert.AreEqual(60, assertion.CommandArgs.Timeout);
        Assert.AreEqual(0, assertion.CommandArgs.ExpectedExitCode);
    }

    [TestMethod]
    [DataRow("{}", "config.program")]
    [DataRow("{program: node, shell: true}", "not supported")]
    [DataRow("{program: node, env: {KEY: value}}", "not supported")]
    [DataRow("{program: node, sub_path: nested}", "not supported")]
    public void RejectsUnsupportedProgramConfiguration(string config, string message)
    {
        var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
            EvalSchema.ParseEvalConfigFlexible($$"""
                stimuli:
                  - name: Check execution
                    prompt: Inspect the launch.
                    graders:
                      - type: program
                        config: {{config}}
                """));
        Assert.Contains(message, error.Message);
    }

    [TestMethod]
    public async Task MissingMetricsCannotPassAProgramGrader()
    {
        var result = Assert.ContainsSingle(await AssertionEvaluator.EvaluateAssertions(
            [Program("")], "", _workspace.FullName));
        Assert.IsFalse(result.Passed);
        Assert.Contains("evaluator-owned trajectory", result.Message);
    }

    [TestMethod]
    public async Task InputIsEvaluatorOwnedAndRemovedAfterGrading()
    {
        string[] keys =
        [
            "EVALUATE_GRADER_INPUT", "EVALUATE_WORKSPACE", "EVALUATE_ASSETS",
            "EVALUATE_ARTIFACT_DIR", "EVALUATE_ARTIFACT_DIR_STRICT",
        ];
        var original = keys.ToDictionary(key => key, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var key in keys)
                Environment.SetEnvironmentVariable(key, "agent-controlled");
            var metrics = new RunMetrics { AgentOutput = "observed response" };
            metrics.Events.Add(new AgentEvent("tool.execution_start", 1,
                new() { ["toolCallId"] = JsonValue.Create("actual-call") }));
            var result = Assert.ContainsSingle(await AssertionEvaluator.EvaluateAssertions(
                [Program("""
                    const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
                    const file=process.env.EVALUATE_GRADER_INPUT;
                    const input=JSON.parse(fs.readFileSync(file,'utf8'));
                    const workspace=fs.realpathSync(process.cwd());
                    assert.equal(fs.realpathSync(process.env.EVALUATE_WORKSPACE),workspace);
                    const relative=path.relative(workspace,fs.realpathSync(file));
                    assert.ok(relative.startsWith('..'+path.sep)||path.isAbsolute(relative));
                    assert.equal(input.trajectory.events[0].data.toolCallId,'actual-call');
                    assert.equal(input.trajectory.output,'observed response');
                    for(const key of ['EVALUATE_ASSETS','EVALUATE_ARTIFACT_DIR','EVALUATE_ARTIFACT_DIR_STRICT'])
                      assert.equal(process.env[key],undefined);
                    fs.writeFileSync('input-path.txt',file);
                    """)], metrics.AgentOutput, _workspace.FullName, metrics: metrics));
            Assert.IsTrue(result.Passed, result.Message);
            var inputPath = await File.ReadAllTextAsync(Path.Combine(_workspace.FullName, "input-path.txt"));
            Assert.IsFalse(File.Exists(inputPath));
            Assert.IsFalse(Directory.Exists(Path.GetDirectoryName(inputPath)));
        }
        finally
        {
            foreach (var (key, value) in original)
                Environment.SetEnvironmentVariable(key, value);
        }
    }

    [TestMethod]
    public async Task ConcurrentGradersReceiveDistinctTrajectoryInputs()
    {
        var assertions = new[] { "first", "second" }.Select(async name =>
        {
            var literal = JsonSerializer.Serialize(name, SkillValidatorJsonContext.Default.String);
            var script = $$"""
                const fs=require('node:fs'),assert=require('node:assert/strict');
                const file=process.env.EVALUATE_GRADER_INPUT;
                const input=JSON.parse(fs.readFileSync(file,'utf8'));
                assert.equal(input.trajectory.output,{{literal}});
                fs.writeFileSync({{literal}}+'.txt',file);
                """;
            return Assert.ContainsSingle(await AssertionEvaluator.EvaluateAssertions(
                [Program(script)], name, _workspace.FullName, metrics: new RunMetrics { AgentOutput = name }));
        });
        foreach (var result in await Task.WhenAll(assertions))
            Assert.IsTrue(result.Passed, result.Message);
        var first = await File.ReadAllTextAsync(Path.Combine(_workspace.FullName, "first.txt"));
        var second = await File.ReadAllTextAsync(Path.Combine(_workspace.FullName, "second.txt"));
        Assert.AreNotEqual(first, second);
        Assert.IsFalse(File.Exists(first));
        Assert.IsFalse(File.Exists(second));
    }

    [TestMethod]
    [DataRow("", true)]
    [DataRow("""{"name":"check","kind":"code","passed":true,"score":1,"evidence":"verified"}""", true)]
    [DataRow("""{"name":"check","kind":"code","passed":false,"score":0,"evidence":"missing launch"}""", false)]
    [DataRow("""{"name":"check","kind":"code","passed":true,"score":1,"evidence":"failed","status":"error"}""", false)]
    [DataRow("""{"name":"check","kind":"code","passed":true,"score":2,"evidence":"invalid score"}""", false)]
    [DataRow("{}", false)]
    [DataRow("not JSON", false)]
    public async Task HonorsExitStatusAndStructuredResults(string output, bool passed)
    {
        var literal = JsonSerializer.Serialize(output, SkillValidatorJsonContext.Default.String);
        var result = Assert.ContainsSingle(await AssertionEvaluator.EvaluateAssertions(
            [Program($"process.stdout.write({literal});")], "", _workspace.FullName, metrics: new RunMetrics()));
        Assert.AreEqual(passed, result.Passed, result.Message);
    }

    [TestMethod]
    public async Task NonzeroExitFailsAndRemovesInput()
    {
        var result = Assert.ContainsSingle(await AssertionEvaluator.EvaluateAssertions(
            [Program("""
                require('node:fs').writeFileSync('input-path.txt',process.env.EVALUATE_GRADER_INPUT);
                process.exit(7);
                """)], "", _workspace.FullName, metrics: new RunMetrics()));
        Assert.IsFalse(result.Passed);
        Assert.Contains("exited with code 7", result.Message);
        Assert.IsFalse(File.Exists(await File.ReadAllTextAsync(Path.Combine(_workspace.FullName, "input-path.txt"))));
    }

    [TestMethod]
    public async Task TimeoutFailsAndRemovesInput()
    {
        var result = Assert.ContainsSingle(await AssertionEvaluator.EvaluateAssertions(
            [Program("""
                require('node:fs').writeFileSync('input-path.txt',process.env.EVALUATE_GRADER_INPUT);
                setTimeout(()=>{},60000);
                """, timeout: 1)], "", _workspace.FullName, metrics: new RunMetrics()));
        Assert.IsFalse(result.Passed);
        Assert.Contains("timed out after 1s", result.Message);
        Assert.IsFalse(File.Exists(await File.ReadAllTextAsync(Path.Combine(_workspace.FullName, "input-path.txt"))));
    }

    [TestMethod]
    [DataRow("msbuild-agent.eval.yaml")]
    [DataRow("build-perf-agent.eval.yaml")]
    [DataRow("binlog-launch.eval.yaml")]
    public async Task LaunchGoldenPassesButForgedRecordsWithoutExecutionFail(string fixture)
    {
        var fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
        var source = Path.Combine(fixtures, "launch-failure");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(_workspace.FullName, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        var config = EvalSchema.ParseEvalConfigFlexible(await File.ReadAllTextAsync(Path.Combine(fixtures, fixture)));
        var assertion = Assert.ContainsSingle(config!.Scenarios[0].Assertions!
            .Where(item => item.Type == AssertionType.Program));
        var reference = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixtures, "launch-reference.json")))!;
        var metrics = new RunMetrics();
        foreach (var step in reference["steps"]!.AsArray())
        {
            if (step!["tool_calls"] is not JsonArray calls)
                continue;
            foreach (var call in calls)
            {
                var command = call!["arguments"]!["command"]!.GetValue<string>();
                var info = new ProcessStartInfo
                {
                    FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
                    WorkingDirectory = _workspace.FullName,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                if (OperatingSystem.IsWindows())
                    info.Arguments = $"/d /s /c \"{command}\"";
                else
                {
                    info.ArgumentList.Add("-c");
                    info.ArgumentList.Add(command);
                }
                using var process = Process.Start(info)!;
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try
                {
                    await process.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    process.Kill(entireProcessTree: true);
                    throw;
                }
                var observed = new JsonObject
                {
                    ["stdout"] = (await stdout).Replace("\r\n", "\n"),
                    ["stderr"] = (await stderr).Replace("\r\n", "\n"),
                    ["exitCode"] = process.ExitCode,
                };
                var expected = JsonNode.Parse(step["observation"]!["results"]![0]!["content"]!.GetValue<string>());
                Assert.IsTrue(JsonNode.DeepEquals(expected, observed), observed.ToJsonString());
                var identity = call["tool_call_id"]!.GetValue<string>();
                metrics.Events.Add(new AgentEvent("tool.execution_start", 0, new()
                {
                    ["toolCallId"] = JsonValue.Create(identity),
                    ["toolName"] = JsonValue.Create("powershell"),
                    ["arguments"] = JsonValue.Create(call["arguments"]!.ToJsonString()),
                }));
                metrics.Events.Add(new AgentEvent("tool.execution_complete", 1, new()
                {
                    ["toolCallId"] = JsonValue.Create(identity),
                    ["result"] = JsonValue.Create(observed.ToJsonString()),
                }));
            }
        }
        var golden = Assert.ContainsSingle(await AssertionEvaluator.EvaluateAssertions(
            [assertion], "", _workspace.FullName, metrics: metrics));
        Assert.IsTrue(golden.Passed, golden.Message);
        metrics.Events.Clear();
        var forged = Assert.ContainsSingle(await AssertionEvaluator.EvaluateAssertions(
            [assertion], "", _workspace.FullName, metrics: metrics));
        Assert.IsFalse(forged.Passed);
        Assert.Contains("Missing correlated launcher execution evidence", forged.Message);
    }
}
