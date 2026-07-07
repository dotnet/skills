using System.CommandLine;
using SkillValidator.Check;
using SkillValidator.Evaluate;

var rootCommand = new RootCommand("Validate that agent skills meaningfully improve agent performance");
rootCommand.Add(EvaluateCommand.Create());
rootCommand.Add(CheckCommand.Create());
rootCommand.Add(ListModelsCommand.Create());

var parseResult = rootCommand.Parse(args);
return await parseResult.InvokeAsync();
