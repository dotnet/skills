using System.CommandLine;
using SkillValidator.Commands;

var rootCommand = new RootCommand("Validate agent skills — use 'check' for static analysis or 'eval' for LLM-based testing");
rootCommand.Add(CheckCommand.Create());
rootCommand.Add(EvaluateCommand.Create());
rootCommand.Add(ConsolidateCommand.Create());

var parseResult = rootCommand.Parse(args);
return await parseResult.InvokeAsync();
