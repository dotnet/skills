using System.CommandLine;
using SkillValidator.Models;
using SkillValidator.Services;

namespace SkillValidator.Commands;

public static class ConsolidateCommand
{
    public static Command Create()
    {
        var filesArg = new Argument<string[]>("files") { Description = "Paths to results.json files to merge" };
        var outputOpt = new Option<string>("--output") { Description = "Output file path for the consolidated markdown", Required = true };

        var command = new Command("consolidate", "Consolidate multiple results.json files into a single markdown summary")
        {
            filesArg,
            outputOpt,
        };

        command.SetAction(async (parseResult, _) =>
        {
            var files = parseResult.GetValue(filesArg) ?? [];
            var output = parseResult.GetValue(outputOpt)!;
            return await Consolidate(files, output);
        });

        return command;
    }

    private static async Task<int> Consolidate(string[] files, string outputPath)
    {
        if (files.Length == 0)
        {
            await File.WriteAllTextAsync(outputPath, "## Skill Validation Results\n\nNo results were produced.\n");
            Console.WriteLine($"No input files provided. Wrote fallback to {outputPath}");
            return 0;
        }

        var allVerdicts = new List<SkillVerdict>();
        string? model = null;
        string? judgeModel = null;

        foreach (var file in files)
        {
            try
            {
                var content = await File.ReadAllTextAsync(file);
                var data = System.Text.Json.JsonSerializer.Deserialize(content,
                    SkillValidatorJsonContext.Default.ConsolidateData);
                if (data?.Verdicts is not null)
                    allVerdicts.AddRange(data.Verdicts);
                if (data?.Model is not null && model is null) model = data.Model;
                if (data?.JudgeModel is not null && judgeModel is null) judgeModel = data.JudgeModel;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"Failed to parse {file}: {error}");
            }
        }

        var output = Reporter.GenerateMarkdownSummary(allVerdicts, model, judgeModel);
        await File.WriteAllTextAsync(outputPath, output);
        Console.WriteLine($"Consolidated {files.Length} result file(s) into {outputPath}");
        return 0;
    }
}
