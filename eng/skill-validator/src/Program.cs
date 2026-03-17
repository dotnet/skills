using System.CommandLine;
using SkillValidator.Commands;

var rootCommand = EvaluateCommand.Create();
rootCommand.Add(CheckCommand.Create());
rootCommand.Add(ConsolidateCommand.Create());
rootCommand.Add(RejudgeCommand.Create());

var parseResult = rootCommand.Parse(args);
return await parseResult.InvokeAsync();
