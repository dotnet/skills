using BuildTaskContracts;
using Microsoft.Build.Framework;
using System.IO;

namespace BuildTasks;

/// <summary>Writes the gateway manifest during the consuming project's build.</summary>
public sealed class ManifestGeneratorTask : Microsoft.Build.Utilities.Task
{
    /// <summary>Gets or sets the generated manifest path.</summary>
    [Required]
    public string OutputFile { get; set; } = "";

    /// <inheritdoc />
    public override bool Execute()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(OutputFile)!);
        File.WriteAllText(OutputFile, ManifestSpec.Header);
        return true;
    }
}
