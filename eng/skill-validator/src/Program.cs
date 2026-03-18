using System.CommandLine;
using SkillValidator.Commands;

var rootCommand = new RootCommand("Validate that agent skills meaningfully improve agent performance");
rootCommand.Add(EvaluateCommand.Create());
rootCommand.Add(CheckCommand.Create());
rootCommand.Add(ConsolidateCommand.Create());
rootCommand.Add(RejudgeCommand.Create());

var parseResult = rootCommand.Parse(args);
return await parseResult.InvokeAsync();
